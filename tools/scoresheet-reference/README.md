# Scoresheet reference parser

`scoresheet_reference.py` is the proof-of-concept for `backend/StatsHub.Api/Scoresheets/ScoresheetParser.cs`.
It reads the league's digital scoresheet PDF from its vector data (text positions + drawn marks),
with no OCR or AI, and cross-checks the result against the printed quarter and final scores.

    pip install pdfplumber
    python scoresheet_reference.py game.pdf --full

Reading rules: filled dot = free throw, diagonal slash = field goal (3 if the scorer's number is
circled), circle on the running-score number = end of period.

Keep the C# parser and this file in step. The expected output used by the tests
(`StatsHub.Api.Tests/Scoresheets/Fixtures/*.expected.json`) came from this script and was checked
by hand against the printed sheet.
