using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Graphics;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Graphics.Colors;

namespace StatsHub.Api.Scoresheets;

/// <summary>
/// Reads an IBBA/Swish digital scoresheet PDF from its vector data - text positions and the
/// drawn marks - with no OCR and no AI, so the same file always gives the same result.
///
/// How the running score is read (confirmed with the league's own sheets):
///   filled dot                          = free throw (1 point)
///   diagonal slash                      = field goal (2, or 3 when the scorer's number is circled)
///   circle on the scorer's number       = three-pointer
///   circle on the running-score number  = end of a period (NOT a three-pointer)
/// A team's running score is 1..160 in four blocks of 40 rows, each row with an A and a B column;
/// the scorer's jersey number is written beside the marked score. Points of a basket are the
/// difference between the marked score and the team's previous marked score.
///
/// Every result is cross-checked (player points vs. running score vs. printed quarter and final
/// scores). Anything that does not add up lands in <see cref="ParsedScoresheet.Errors"/>.
///
/// The geometry mirrors tools/scoresheet-reference/scoresheet_reference.py, which was verified
/// against a real sheet; keep the two in step. Coordinates are in points, y growing downwards.
/// </summary>
public static class ScoresheetParser
{
    private static readonly Regex HebrewChar = new(@"[\u0590-\u05FF]", RegexOptions.Compiled);
    private static readonly Regex CodeToken = new(@"^\(\d+\)$", RegexOptions.Compiled);
    private static readonly Regex DateToken = new(@"^\d\d/\d\d/\d{4}$", RegexOptions.Compiled);
    private static readonly Regex TimeToken = new(@"^\d\d:\d\d$", RegexOptions.Compiled);
    private static readonly Regex FoulToken = new(@"^[PTUD]\d?$", RegexOptions.Compiled);

    public static ParsedScoresheet Parse(byte[] pdf)
    {
        using var doc = PdfDocument.Open(pdf);
        return Parse(doc.GetPage(1));
    }

    // ------------------------------------------------------------------ geometry types

    private readonly record struct Rgb(double R, double G, double B)
    {
        public bool IsBlack => R < 0.05 && G < 0.05 && B < 0.05;
        public bool IsWhite => R > 0.95 && G > 0.95 && B > 0.95;
        // Background tints (the grid's cells are 0.93/0.94/0.95), never ink.
        public bool IsLight => R > 0.85 && G > 0.85 && B > 0.85;
        public bool IsBlue => R < 0.05 && G < 0.05 && B > 0.95;
    }

    private sealed record Word(string Text, double X0, double X1, double Base, double Size, Rgb Color)
    {
        public double Top => Base - Size;
        public double Cx => (X0 + X1) / 2;
        public double Cy => Base - Size / 2;
    }

    // IsCurve: what pdfplumber calls a "curve" - anything but an axis-aligned rectangle or a
    // straight horizontal/vertical line. The grid's own cell boxes are rectangles; the marks
    // (dots, circles, the diagonal slash) are curves.
    private sealed record Shape(double X0, double X1, double Top, double Bottom, bool Filled, bool Stroked, Rgb Fill, bool IsCurve)
    {
        public double W => X1 - X0;
        public double H => Bottom - Top;
        public double Cx => (X0 + X1) / 2;
        public double Cy => (Top + Bottom) / 2;
    }

    private readonly record struct Cell(double Cx, double Cy);

    private readonly record struct LetterInfo(string Value, double X0, double X1, double Base, double Size, Rgb Color);

    // NumberText is the jersey as printed: "0" and "00" are different players, so baskets are
    // matched to players by it, not by the number.
    private sealed record RosterRow(int Number, string NumberText, string Code, string LastName, string FirstInitial,
        bool Captain, bool Played, bool Starter, int Fouls);

    // ------------------------------------------------------------------ extraction

    private static Rgb ToRgb(IColor? color)
    {
        if (color is null) return new Rgb(0, 0, 0);
        var (r, g, b) = color.ToRGBValues();
        return new Rgb((double)r, (double)g, (double)b);
    }

    /// <summary>
    /// Groups letters into words the way the reference does: letters on one baseline (3pt chain),
    /// ordered left to right, split at a gap over 3pt, at a space, or at a colour change.
    /// Hebrew therefore comes out in visual order (reversed) - <see cref="Logical"/> flips it back.
    /// </summary>
    private static List<Word> BuildWords(Page page)
    {
        double height = page.Height;
        var letters = page.Letters
            .Select(l => new LetterInfo(
                l.Value ?? string.Empty,
                Math.Min(l.StartBaseLine.X, l.EndBaseLine.X),
                Math.Max(l.StartBaseLine.X, l.EndBaseLine.X),
                height - l.StartBaseLine.Y,
                l.PointSize,
                ToRgb(l.Color)))
            .OrderBy(l => l.Base)
            .ToList();

        var lines = new List<List<LetterInfo>>();
        foreach (var letter in letters)
        {
            if (lines.Count > 0 && letter.Base - lines[^1][^1].Base <= 3) lines[^1].Add(letter);
            else lines.Add(new List<LetterInfo> { letter });
        }

        var words = new List<Word>();
        foreach (var line in lines)
        {
            var sb = new StringBuilder();
            double x0 = 0, x1 = 0, baseline = 0, size = 0;
            Rgb color = default;
            bool open = false;

            void Flush()
            {
                if (open && sb.Length > 0) words.Add(new Word(sb.ToString(), x0, x1, baseline, size, color));
                sb.Clear();
                open = false;
            }

            foreach (var l in line.OrderBy(l => l.X0))
            {
                if (string.IsNullOrWhiteSpace(l.Value)) { Flush(); continue; }
                if (open && (l.X0 - x1 > 3 || l.Color != color)) Flush();
                if (!open)
                {
                    open = true;
                    x0 = l.X0; x1 = l.X1; baseline = l.Base; size = l.Size; color = l.Color;
                }
                sb.Append(l.Value);
                x1 = Math.Max(x1, l.X1);
            }
            Flush();
        }
        return words;
    }

    private static List<Shape> BuildShapes(Page page)
    {
        double height = page.Height;
        var shapes = new List<Shape>();
        foreach (var path in page.Paths)
        {
            var rect = path.GetBoundingRectangle();
            if (rect is null) continue;
            var r = rect.Value;
            shapes.Add(new Shape(r.Left, r.Right, height - r.Top, height - r.Bottom,
                path.IsFilled, path.IsStroked, path.IsFilled ? ToRgb(path.FillColor) : default, IsCurve(path)));
        }
        return shapes;
    }

    private static bool IsCurve(PdfPath path)
    {
        foreach (var subpath in path)
        {
            PdfPoint? start = null, current = null;
            foreach (var command in subpath.Commands)
            {
                switch (command)
                {
                    case PdfSubpath.BezierCurve:
                        return true;
                    case PdfSubpath.Move move:
                        start = current = move.Location;
                        break;
                    case PdfSubpath.Line line:
                        if (Diagonal(line.From, line.To)) return true;
                        current = line.To;
                        break;
                    case PdfSubpath.Close:
                        if (start is not null && current is not null && Diagonal(current.Value, start.Value)) return true;
                        current = start;
                        break;
                }
            }
        }
        return false;
    }

    private static bool Diagonal(PdfPoint a, PdfPoint b) =>
        Math.Abs(a.X - b.X) > 0.01 && Math.Abs(a.Y - b.Y) > 0.01;

    /// <summary>Hebrew arrives visually ordered (reversed); flip it, mirroring brackets.</summary>
    private static string Logical(Word w)
    {
        if (!HebrewChar.IsMatch(w.Text)) return w.Text;
        var chars = w.Text.Reverse().Select(c => c == '(' ? ')' : c == ')' ? '(' : c).ToArray();
        return new string(chars);
    }

    /// <summary>Joins the words of one right-to-left field, rightmost word first.</summary>
    private static string ReadRtl(IEnumerable<Word> words) =>
        string.Join(" ", words.OrderByDescending(w => w.X1).Select(Logical));

    private static bool TryInt(string text, out int value)
    {
        value = 0;
        return text.Length is > 0 and <= 3 && text.All(c => c is >= '0' and <= '9')
            && int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }

    // ------------------------------------------------------------------ parsing

    private static ParsedScoresheet Parse(Page page)
    {
        var words = BuildWords(page);
        var shapes = BuildShapes(page);
        var errors = new List<string>();
        var warnings = new List<string>();

        // ---- header
        var headNames = words.Where(w => w.Color.IsBlue && w.Top is > 38 and < 50).ToList();
        string homeName = ReadRtl(headNames.Where(w => w.X0 < 300));
        string awayName = ReadRtl(headNames.Where(w => w.X0 >= 300));
        string? date = words.FirstOrDefault(w => DateToken.IsMatch(w.Text))?.Text;
        string? time = words.FirstOrDefault(w => TimeToken.IsMatch(w.Text) && w.Top < 70)?.Text;
        string competition = ReadRtl(words.Where(w => w.Color.IsBlue && w.Top is > 56 and < 70 && w.X0 < 270));

        // ---- running-score grid: each number 1..160 exists once per team column
        var cells = new Dictionary<(char Side, int N), Cell>();
        var gridWords = words
            .Where(w => TryInt(w.Text, out var n) && n is >= 1 and <= 160 && w.Color.IsBlack
                        && w.X0 > 270 && w.Top is > 115 and < 600)
            .GroupBy(w => int.Parse(w.Text, CultureInfo.InvariantCulture));
        foreach (var g in gridWords)
        {
            var pair = g.OrderBy(w => w.X0).ToList();
            if (pair.Count != 2)
            {
                errors.Add($"running-score number {g.Key} found {pair.Count} times (expected 2)");
                continue;
            }
            cells[('A', g.Key)] = new Cell(pair[0].Cx, pair[0].Cy);
            cells[('B', g.Key)] = new Cell(pair[1].Cx, pair[1].Cy);
        }
        int gridRows = cells.Keys.Select(k => k.N).Distinct().Count();
        if (gridRows != 160) errors.Add($"expected 160 grid rows, found {gridRows}");

        ((char Side, int N)? Key, double Dist) NearestCell(double cx, double cy)
        {
            (char Side, int N)? best = null;
            double bestD = double.MaxValue;
            foreach (var (key, c) in cells)
            {
                double d = Math.Sqrt((c.Cx - cx) * (c.Cx - cx) + (c.Cy - cy) * (c.Cy - cy));
                if (d < bestD) { best = key; bestD = d; }
            }
            return (best, bestD);
        }

        // ---- scorer jersey numbers: coloured numbers beside a grid row
        var scorer = new Dictionary<(char Side, int N), int>();
        var scorerText = new Dictionary<(char Side, int N), string>();
        var scorerCenters = new Dictionary<(char Side, int N), Cell>();
        foreach (var w in words.Where(w => TryInt(w.Text, out _) && !w.Color.IsBlack && !w.Color.IsWhite
                                           && w.X0 > 255 && w.Top is > 115 and < 600))
        {
            var candidates = cells.Where(c => Math.Abs(w.Cy - c.Value.Cy) < 4.5)
                .Select(c => (Dist: Math.Abs(w.Cx - c.Value.Cx), Key: c.Key))
                .OrderBy(c => c.Dist).ToList();
            if (candidates.Count == 0 || candidates[0].Dist > 40) continue;
            var key = candidates[0].Key;
            if (scorer.ContainsKey(key)) errors.Add($"two scorer numbers on row {key}");
            scorer[key] = int.Parse(w.Text, CultureInfo.InvariantCulture);
            scorerText[key] = w.Text;
            scorerCenters[key] = new Cell(w.Cx, w.Cy);
        }

        // ---- marks: dots, slashes, circles
        var marks = new Dictionary<(char Side, int N), string>();
        var circledScorer = new HashSet<(char Side, int N)>();
        var periodEnds = new Dictionary<char, List<int>> { ['A'] = new(), ['B'] = new() };
        foreach (var s in shapes.Where(s => s.X0 > 255 && s.Top is > 115 and < 600))
        {
            // A slash is a filled 4-point polygon in ink. PdfPig stores any closed 4-line path as an
            // axis-aligned rectangle, so its angle is lost - the grid's own cell backgrounds have the
            // same size but a light grey fill, which is what tells them apart here.
            if (s.Filled && !s.Stroked && !s.Fill.IsLight && s.W is > 17 and < 21 && s.H is > 11 and < 15)
                AddMark("slash", s);
            else if (!s.IsCurve) continue;
            else if (s.Filled && !s.Stroked && !s.Fill.IsWhite && s.W is > 4 and < 7 && s.H is > 4 and < 7)
                AddMark("dot", s);
            else if (s.Stroked && !s.Filled && s.W is > 9 and < 11.5)
            {
                var (key, dist) = NearestCell(s.Cx, s.Cy);
                if (key is not null && dist < 4)
                {
                    periodEnds[key.Value.Side].Add(key.Value.N);
                    continue;
                }
                var hit = scorerCenters.Where(c => Math.Sqrt(Math.Pow(c.Value.Cx - s.Cx, 2) + Math.Pow(c.Value.Cy - s.Cy, 2)) < 4)
                    .Select(c => c.Key).ToList();
                if (hit.Count > 0) circledScorer.Add(hit[0]);
                else warnings.Add($"unclassified circle at {s.Cx:0},{s.Cy:0}");
            }
        }

        void AddMark(string kind, Shape s)
        {
            var (key, dist) = NearestCell(s.Cx, s.Cy);
            if (key is null || dist > 10) { errors.Add($"unmatched {kind} at {s.Cx:0},{s.Cy:0}"); return; }
            if (marks.ContainsKey(key.Value)) errors.Add($"two marks on cell {key.Value}");
            marks[key.Value] = kind;
        }

        // ---- printed quarter scores and final score
        var quarters = new List<QuarterScore>();
        {
            int? qHome = null, qAway = null;
            bool inGroup = false;
            bool onAway = false;
            foreach (var w in words.Where(w => w.Top is > 615 and < 630 && w.X0 > 262).OrderBy(w => w.X0))
            {
                if (w.Text == "A")
                {
                    if (inGroup) quarters.Add(new QuarterScore(qHome, qAway));
                    qHome = null; qAway = null; inGroup = true; onAway = false;
                }
                else if (w.Text == "B") onAway = true;
                else if (inGroup && TryInt(w.Text, out var value))
                {
                    if (onAway) qAway = value; else qHome = value;
                }
            }
            if (inGroup) quarters.Add(new QuarterScore(qHome, qAway));
            quarters = quarters.Where(q => q.Home is not null || q.Away is not null).ToList();
        }
        int? homeFinal = null, awayFinal = null;
        foreach (var w in words.Where(w => w.Color.IsBlue && TryInt(w.Text, out _)
                                           && w.Top is > 660 and < 692 && w.X0 is > 315 and < 345))
        {
            int n = int.Parse(w.Text, CultureInfo.InvariantCulture);
            if (w.Top < 677) homeFinal = n; else awayFinal = n;
        }

        // ---- rosters
        var markerA = words.Where(w => w.Text == "(A)" && w.X0 < 100).OrderByDescending(w => w.Top).FirstOrDefault();
        var markerB = words.Where(w => w.Text == "(B)" && w.X0 < 100).OrderByDescending(w => w.Top).FirstOrDefault();
        var rosters = new Dictionary<char, List<RosterRow>> { ['A'] = new(), ['B'] = new() };
        if (markerA is null || markerB is null)
        {
            errors.Add("could not locate the roster sections");
        }
        else
        {
            var xCircles = shapes.Where(s => s.IsCurve && s.Stroked && !s.Filled && s.W is > 8 and < 10.5 && s.X0 < 200).ToList();
            foreach (var code in words.Where(w => CodeToken.IsMatch(w.Text) && w.X0 < 60 && w.Top > markerA.Top)
                         .OrderBy(w => w.Top))
            {
                char side = code.Top < markerB.Top ? 'A' : 'B';
                var band = words.Where(w => w.Top >= code.Top - 4 && w.Top <= code.Top + 9 && w.X0 < 262).ToList();
                var number = band.FirstOrDefault(w => TryInt(w.Text, out _) && w.X0 is > 130 and < 142);
                if (number is null) continue; // coach rows carry a code but no jersey number

                bool captain = band.Any(w => Regex.IsMatch(w.Text, @"^\((CAP|C)\),?$"));
                var nameWords = band.Where(w => HebrewChar.IsMatch(w.Text) && w.X0 > 47).ToList();
                var tokens = Regex.Replace(ReadRtl(nameWords), @"[.,]", " ")
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var single = tokens.Select((t, i) => (t, i)).Where(x => x.t.Length == 1).ToList();
                string initial = tokens.Length > 1 && single.Count > 0 ? single[^1].t : string.Empty;
                string last = string.Join(" ", initial.Length > 0 ? tokens.Where((_, i) => i != single[^1].i) : tokens);

                var x = band.FirstOrDefault(w => w.Text == "X" && w.X0 is > 146 and < 160);
                bool played = x is not null;
                bool starter = false;
                if (x is not null)
                {
                    Word xMark = x;
                    starter = xCircles.Any(c => Math.Sqrt(Math.Pow(c.Cx - xMark.Cx, 2) + Math.Pow(c.Cy - xMark.Cy, 2)) < 4);
                }
                int fouls = band.Count(w => FoulToken.IsMatch(w.Text) && w.X0 > 160);

                rosters[side].Add(new RosterRow(int.Parse(number.Text, CultureInfo.InvariantCulture), number.Text,
                    code.Text.Trim('(', ')'), last, initial, captain, played, starter, fouls));
            }
        }

        // ---- scoring events per team
        var events = new Dictionary<char, List<ScoringEvent>> { ['A'] = new(), ['B'] = new() };
        // The scorer's jersey as written, per basket (side, running score).
        var eventScorer = new Dictionary<(char Side, int Score), string>();
        foreach (char side in new[] { 'A', 'B' })
        {
            int prev = 0;
            foreach (var key in marks.Keys.Where(k => k.Side == side).OrderBy(k => k.N))
            {
                int diff = key.N - prev;
                bool dot = marks[key] == "dot";
                int points = dot ? 1 : circledScorer.Contains(key) ? 3 : 2;
                int? player = scorer.TryGetValue(key, out var p) ? p : null;
                if (scorerText.TryGetValue(key, out var text)) eventScorer[(side, key.N)] = text;
                if (player is null) errors.Add($"{side} score {key.N}: no scorer number");
                if (diff != points)
                    errors.Add($"{side} score {key.N}: mark implies {points} pts but score moved {diff}");
                events[side].Add(new ScoringEvent(key.N, player, points, dot ? "ft" : points == 3 ? "fg3" : "fg2", 0));
                prev = key.N;
            }

            // period of each basket: from the printed quarter scores
            var cumulative = new List<int>();
            int running = 0;
            foreach (var q in quarters)
            {
                running += (side == 'A' ? q.Home : q.Away) ?? 0;
                cumulative.Add(running);
            }
            events[side] = events[side].Select(e =>
            {
                int idx = cumulative.FindIndex(c => e.Score <= c);
                return e with { Period = idx >= 0 ? idx + 1 : cumulative.Count + 1 };
            }).ToList();

            var ends = periodEnds[side].OrderBy(n => n).ToList();
            if (cumulative.Count > 0 && !ends.Take(cumulative.Count).SequenceEqual(cumulative.Take(ends.Count)))
                warnings.Add($"{side}: circled period-end scores [{string.Join(",", ends)}] differ from printed cumulative [{string.Join(",", cumulative)}]");
        }

        // ---- per-player totals and cross-checks
        ScoresheetTeam BuildTeam(char side, string name, int? printedFinal)
        {
            var players = rosters[side].Select(r =>
            {
                var mine = events[side].Where(e => eventScorer.GetValueOrDefault((side, e.Score)) == r.NumberText).ToList();
                return new ScoresheetPlayer(r.Number, r.Code, r.LastName, r.FirstInitial, r.Captain, r.Played,
                    r.Starter, r.Fouls, mine.Sum(e => e.Points),
                    mine.Count(e => e.Kind == "ft"), mine.Count(e => e.Kind == "fg2"), mine.Count(e => e.Kind == "fg3"));
            }).ToList();

            var numbers = rosters[side].Select(r => r.NumberText).ToHashSet();
            foreach (var e in events[side].Where(e => eventScorer.TryGetValue((side, e.Score), out var t) && !numbers.Contains(t)))
                errors.Add($"{side}: scorer #{eventScorer[(side, e.Score)]} not on roster");

            int total = players.Sum(p => p.Points);
            int lastScore = events[side].Count > 0 ? events[side][^1].Score : 0;
            if (total != lastScore) errors.Add($"{side}: player points {total} != last running score {lastScore}");
            if (printedFinal is not null && printedFinal != lastScore)
                errors.Add($"{side}: printed final {printedFinal} != running score {lastScore}");
            for (int qi = 0; qi < quarters.Count; qi++)
            {
                int? printed = side == 'A' ? quarters[qi].Home : quarters[qi].Away;
                int got = events[side].Where(e => e.Period == qi + 1).Sum(e => e.Points);
                if (printed is not null && got != printed)
                    errors.Add($"{side} Q{qi + 1}: events {got} != printed {printed}");
            }
            return new ScoresheetTeam(name, lastScore, players, events[side]);
        }

        var homeTeam = BuildTeam('A', homeName, homeFinal);
        var awayTeam = BuildTeam('B', awayName, awayFinal);
        return new ParsedScoresheet(competition.Length > 0 ? competition : null, date, time, homeTeam, awayTeam,
            quarters, homeFinal, awayFinal, errors, warnings);
    }
}
