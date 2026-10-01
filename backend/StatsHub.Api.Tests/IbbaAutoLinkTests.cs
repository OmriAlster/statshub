using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StatsHub.Api.Data;
using StatsHub.Api.DTOs;
using StatsHub.Api.Models;
using StatsHub.Api.Services;
using StatsHub.Api.Tests.Infrastructure;

namespace StatsHub.Api.Tests;

// Linking a player to IBBA gives each of their IBBA teams an app team with no
// button to press - joining a sibling's team, creating one, or (only when the
// player already has a team not linked to IBBA) leaving it for the parent.
public class IbbaAutoLinkTests : IClassFixture<StatsHubFactory>
{
    private readonly StatsHubFactory _factory;
    public IbbaAutoLinkTests(StatsHubFactory factory) => _factory = factory;

    private async Task<List<IbbaTeam>> SeedIbbaTeamsAsync(params (string Name, string League)[] teams)
    {
        var created = new List<IbbaTeam>();
        await _factory.WithDbAsync(async db =>
        {
            foreach (var (name, league) in teams)
            {
                var team = new IbbaTeam { IbbaTeamId = $"t{Guid.NewGuid():N}", TeamUrl = $"https://example.test/{Guid.NewGuid():N}", Name = name, LeagueName = league, LeagueUrl = $"https://example.test/league/{Guid.NewGuid():N}" };
                db.IbbaTeams.Add(team);
                created.Add(team);
            }
            await db.SaveChangesAsync();
        });
        return created;
    }

    private async Task AutoLinkAsync(Guid playerId, List<IbbaTeam> ibbaTeams)
    {
        using var scope = _factory.Services.CreateScope();
        var service = (IbbaService)scope.ServiceProvider.GetRequiredService<IIbbaService>();
        await service.AutoLinkAppTeamsAsync(playerId, ibbaTeams);
    }

    private async Task<List<Team>> TeamsOfAsync(Guid playerId)
    {
        var teams = new List<Team>();
        await _factory.WithDbAsync(async db =>
            teams.AddRange(await db.PlayerTeams.Where(pt => pt.PlayerId == playerId).Select(pt => pt.Team).OrderBy(t => t.Id).ToListAsync()));
        return teams;
    }

    [Fact]
    public async Task A_new_ibba_player_gets_a_linked_team_automatically()
    {
        var parent = await TestUser.SignInAsync(_factory);
        var player = await parent.CreatePlayerAsync();
        var ibba = await SeedIbbaTeamsAsync(("Maccabi", "U16 North"));

        await AutoLinkAsync(player.Id, ibba);

        var team = (await TeamsOfAsync(player.Id)).Single();
        Assert.Equal("Maccabi", team.Name);
        Assert.Equal(ibba[0].Id, team.IbbaTeamId);
    }

    [Fact]
    public async Task Two_ibba_teams_with_the_same_name_get_the_league_added()
    {
        var parent = await TestUser.SignInAsync(_factory);
        var player = await parent.CreatePlayerAsync();
        var ibba = await SeedIbbaTeamsAsync(("מכבי תל מונד", "נערים א מחוזית שרון"), ("מכבי תל מונד", "נוער ארצית שרון"));

        await AutoLinkAsync(player.Id, ibba);

        Assert.Equivalent(new[] { "מכבי תל מונד - נערים א מחוזית שרון", "מכבי תל מונד - נוער ארצית שרון" }, (await TeamsOfAsync(player.Id)).Select(t => t.Name).ToArray(), strict: true);
    }

    [Fact]
    public async Task A_sibling_on_the_same_ibba_team_gets_their_own_team()
    {
        var parent = await TestUser.SignInAsync(_factory);
        var older = await parent.CreatePlayerAsync("Older");
        var younger = await parent.CreatePlayerAsync("Younger");
        var ibba = await SeedIbbaTeamsAsync(("Hapoel", "U14"));

        await AutoLinkAsync(older.Id, ibba);
        await AutoLinkAsync(younger.Id, ibba);

        var olderTeam = (await TeamsOfAsync(older.Id)).Single();
        var youngerTeam = (await TeamsOfAsync(younger.Id)).Single();
        Assert.NotEqual(olderTeam.Id, youngerTeam.Id); // siblings never affect each other's teams
    }

    [Fact]
    public async Task A_player_with_a_team_not_linked_to_ibba_is_left_for_the_parent_to_decide()
    {
        var parent = await TestUser.SignInAsync(_factory);
        var player = await parent.CreatePlayerAsync();
        var existing = await parent.CreateTeamWithPlayerAsync(player.Id, "School team");
        var ibba = await SeedIbbaTeamsAsync(("Maccabi", "U16"));

        await AutoLinkAsync(player.Id, ibba);

        var teams = await TeamsOfAsync(player.Id);
        Assert.Equal(existing.Id, teams.Single().Id); // nothing created or linked
        Assert.Null(teams.Single().IbbaTeamId);
    }

    [Fact]
    public async Task With_an_existing_team_the_parent_is_asked_and_one_yes_creates_the_rest()
    {
        var parent = await TestUser.SignInAsync(_factory);
        var player = await parent.CreatePlayerAsync();
        var existing = await parent.CreateTeamWithPlayerAsync(player.Id, "School team");
        var ibba = await SeedIbbaTeamsAsync(("Maccabi", "U16"), ("Maccabi", "U18"));
        await _factory.WithDbAsync(async db =>
        {
            var link = new PlayerIbbaLink { PlayerId = player.Id, IbbaPlayerUrl = "https://ibasketball.co.il/player/test/", CreatedAt = DateTime.UtcNow };
            db.PlayerIbbaLinks.Add(link);
            await db.SaveChangesAsync();
            db.PlayerIbbaTeams.AddRange(ibba.Select(t => new PlayerIbbaTeam { PlayerIbbaLinkId = link.Id, IbbaTeamId = t.Id }));
            await db.SaveChangesAsync();
        });

        await AutoLinkAsync(player.Id, ibba);
        Assert.Single(await TeamsOfAsync(player.Id)); // nothing created - the pop-up asks

        // "Yes" for the first IBBA team: it's the existing team.
        // That answers it - the second IBBA team gets its own new team, no more asking.
        var status = await parent.PutAsync<IbbaLinkStatusDto>($"/api/ibba/team-links/{ibba[0].Id}", new LinkIbbaTeamDto { TeamId = existing.Id, PlayerId = player.Id });
        Assert.All(status.Teams, t => Assert.NotNull(t.LinkedTeamId));
        var teams = await TeamsOfAsync(player.Id);
        Assert.Equal(existing.Id, teams.Single(t => t.IbbaTeamId == ibba[0].Id).Id);
        Assert.Equivalent(new[] { ibba[0].Id, ibba[1].Id }, teams.Select(t => t.IbbaTeamId!.Value).ToArray(), strict: true);
    }

    [Fact]
    public async Task Linking_a_player_when_the_family_has_a_team_without_ibba_asks_and_adds_to_it()
    {
        var parent = await TestUser.SignInAsync(_factory);
        var other = await parent.CreatePlayerAsync("Other");
        var player = await parent.CreatePlayerAsync("Linked");
        // A team of yours without IBBA that this player isn't on yet.
        var existing = await parent.CreateTeamWithPlayerAsync(other.Id, "מכבי תל מונד");
        await _factory.WithDbAsync(async db =>
        {
            db.PlayerTeams.RemoveRange(db.PlayerTeams.Where(pt => pt.TeamId == existing.Id));
            await db.SaveChangesAsync();
        });
        var ibba = await SeedIbbaTeamsAsync(("מכבי תל מונד", "U16"));
        await _factory.WithDbAsync(async db =>
        {
            var link = new PlayerIbbaLink { PlayerId = player.Id, IbbaPlayerUrl = "https://ibasketball.co.il/player/test/", CreatedAt = DateTime.UtcNow };
            db.PlayerIbbaLinks.Add(link);
            await db.SaveChangesAsync();
            db.PlayerIbbaTeams.Add(new PlayerIbbaTeam { PlayerIbbaLinkId = link.Id, IbbaTeamId = ibba[0].Id });
            await db.SaveChangesAsync();
        });

        await AutoLinkAsync(player.Id, ibba);
        Assert.Empty(await TeamsOfAsync(player.Id)); // nothing created - the pop-up asks

        var status = await parent.GetAsync<IbbaLinkStatusDto>($"/api/players/{player.Id}/ibba");
        Assert.Contains(status.ExistingTeams, t => t.Id == existing.Id); // offered in the pop-up

        // "Yes, add to this team"
        status = await parent.PutAsync<IbbaLinkStatusDto>($"/api/ibba/team-links/{ibba[0].Id}", new LinkIbbaTeamDto { TeamId = existing.Id, PlayerId = player.Id });
        Assert.Equal(existing.Id, status.Teams.Single().LinkedTeamId);
        var team = (await TeamsOfAsync(player.Id)).Single();
        Assert.Equal(existing.Id, team.Id);
        Assert.Equal(ibba[0].Id, team.IbbaTeamId);
    }

    [Fact]
    public async Task The_pop_up_offers_only_the_players_own_team_when_they_have_one()
    {
        var parent = await TestUser.SignInAsync(_factory);
        var other = await parent.CreatePlayerAsync("Other");
        var player = await parent.CreatePlayerAsync("Linked");
        var familyTeam = await parent.CreateTeamWithPlayerAsync(other.Id, "מכבי תל מונד");
        var ownTeam = await parent.CreateTeamWithPlayerAsync(player.Id, "מכבי תל מונד"); // same name
        var ibba = await SeedIbbaTeamsAsync(("מכבי תל מונד", "U16"));
        await _factory.WithDbAsync(async db =>
        {
            var link = new PlayerIbbaLink { PlayerId = player.Id, IbbaPlayerUrl = "https://ibasketball.co.il/player/test/", CreatedAt = DateTime.UtcNow };
            db.PlayerIbbaLinks.Add(link);
            await db.SaveChangesAsync();
            db.PlayerIbbaTeams.Add(new PlayerIbbaTeam { PlayerIbbaLinkId = link.Id, IbbaTeamId = ibba[0].Id });
            await db.SaveChangesAsync();
        });

        var status = await parent.GetAsync<IbbaLinkStatusDto>($"/api/players/{player.Id}/ibba");

        Assert.Equal([ownTeam.Id], status.ExistingTeams.Select(t => t.Id).ToArray());
        Assert.DoesNotContain(status.ExistingTeams, t => t.Id == familyTeam.Id);
    }

    [Fact]
    public async Task Two_existing_teams_and_two_ibba_teams_are_asked_one_at_a_time()
    {
        var parent = await TestUser.SignInAsync(_factory);
        var player = await parent.CreatePlayerAsync();
        var first = await parent.CreateTeamWithPlayerAsync(player.Id, "Youth");
        var second = await parent.CreateTeamWithPlayerAsync(player.Id, "Juniors");
        var ibba = await SeedIbbaTeamsAsync(("Maccabi", "U16"), ("Maccabi", "U18"));
        await _factory.WithDbAsync(async db =>
        {
            var link = new PlayerIbbaLink { PlayerId = player.Id, IbbaPlayerUrl = "https://ibasketball.co.il/player/test/", CreatedAt = DateTime.UtcNow };
            db.PlayerIbbaLinks.Add(link);
            await db.SaveChangesAsync();
            db.PlayerIbbaTeams.AddRange(ibba.Select(t => new PlayerIbbaTeam { PlayerIbbaLinkId = link.Id, IbbaTeamId = t.Id }));
            await db.SaveChangesAsync();
        });
        await AutoLinkAsync(player.Id, ibba);

        // "Yes" for the first IBBA team - the second is still asked, with the team that's left.
        var status = await parent.PutAsync<IbbaLinkStatusDto>($"/api/ibba/team-links/{ibba[0].Id}", new LinkIbbaTeamDto { TeamId = first.Id, PlayerId = player.Id });
        Assert.Null(status.Teams.Single(t => t.Id == ibba[1].Id).LinkedTeamId);
        Assert.Equal([second.Id], status.ExistingTeams.Select(t => t.Id).ToArray());
        await AutoLinkAsync(player.Id, ibba); // e.g. the nightly sync - still waits

        // "Yes" for the second too.
        status = await parent.PutAsync<IbbaLinkStatusDto>($"/api/ibba/team-links/{ibba[1].Id}", new LinkIbbaTeamDto { TeamId = second.Id, PlayerId = player.Id });
        Assert.Equivalent(new[] { first.Id, second.Id }, status.Teams.Select(t => t.LinkedTeamId!.Value).ToArray(), strict: true);
        Assert.Equal(2, (await TeamsOfAsync(player.Id)).Count); // nothing new created
    }

    [Fact]
    public async Task An_already_linked_ibba_team_is_left_alone()
    {
        var parent = await TestUser.SignInAsync(_factory);
        var player = await parent.CreatePlayerAsync();
        var ibba = await SeedIbbaTeamsAsync(("Maccabi", "U16"));

        await AutoLinkAsync(player.Id, ibba);
        await AutoLinkAsync(player.Id, ibba); // e.g. the nightly sync

        Assert.Single(await TeamsOfAsync(player.Id));
    }

    [Fact]
    public async Task Old_teams_that_share_a_plain_ibba_name_get_the_league_added()
    {
        // Teams made with the old "Create team" button, before the league rule.
        var parent = await TestUser.SignInAsync(_factory);
        var player = await parent.CreatePlayerAsync();
        var ibba = await SeedIbbaTeamsAsync(("מכבי תל מונד", "נערים א מחוזית שרון"), ("מכבי תל מונד", "נוער ארצית שרון"));
        var first = await parent.CreateTeamWithPlayerAsync(player.Id, "מכבי תל מונד");
        var second = await parent.CreateTeamWithPlayerAsync(player.Id, "מכבי תל מונד");
        await _factory.WithDbAsync(async db =>
        {
            (await db.Teams.FindAsync(first.Id))!.IbbaTeamId = ibba[0].Id;
            (await db.Teams.FindAsync(second.Id))!.IbbaTeamId = ibba[1].Id;
            await db.SaveChangesAsync();
        });

        await AutoLinkAsync(player.Id, ibba); // any sync

        Assert.Equivalent(new[] { "מכבי תל מונד - נערים א מחוזית שרון", "מכבי תל מונד - נוער ארצית שרון" }, (await TeamsOfAsync(player.Id)).Select(t => t.Name).ToArray(), strict: true);
    }

    [Fact]
    public async Task A_team_name_the_parent_chose_is_never_changed()
    {
        var parent = await TestUser.SignInAsync(_factory);
        var player = await parent.CreatePlayerAsync();
        var ibba = await SeedIbbaTeamsAsync(("Maccabi", "U16"), ("Maccabi", "U18"));
        var mine = await parent.CreateTeamWithPlayerAsync(player.Id, "Maccabi");
        var custom = await parent.CreateTeamWithPlayerAsync(player.Id, "My kid's team");
        var sameCustom = await parent.CreateTeamWithPlayerAsync(player.Id, "My kid's team");
        await _factory.WithDbAsync(async db =>
        {
            (await db.Teams.FindAsync(mine.Id))!.IbbaTeamId = ibba[0].Id;
            (await db.Teams.FindAsync(custom.Id))!.IbbaTeamId = ibba[1].Id;
            await db.SaveChangesAsync();
        });

        await AutoLinkAsync(player.Id, ibba);

        var names = (await TeamsOfAsync(player.Id)).Select(t => t.Name).ToArray();
        Assert.Equivalent(new[] { "Maccabi", "My kid's team", "My kid's team" }, names, strict: true); // "Maccabi" doesn't clash; the custom names aren't IBBA names
    }

    [Fact]
    public async Task Choosing_create_new_team_in_the_pop_up_creates_and_links_it()
    {
        var parent = await TestUser.SignInAsync(_factory);
        var player = await parent.CreatePlayerAsync();
        await parent.CreateTeamWithPlayerAsync(player.Id, "School team");
        var ibba = await SeedIbbaTeamsAsync(("Maccabi", "U16"));
        await _factory.WithDbAsync(async db =>
        {
            var link = new PlayerIbbaLink { PlayerId = player.Id, IbbaPlayerUrl = "https://ibasketball.co.il/player/test/", CreatedAt = DateTime.UtcNow };
            db.PlayerIbbaLinks.Add(link);
            await db.SaveChangesAsync();
            db.PlayerIbbaTeams.Add(new PlayerIbbaTeam { PlayerIbbaLinkId = link.Id, IbbaTeamId = ibba[0].Id });
            await db.SaveChangesAsync();
        });

        var status = await parent.PostAsync<IbbaLinkStatusDto>($"/api/ibba/team-links/{ibba[0].Id}/new-team", new CreateTeamForIbbaTeamDto { PlayerId = player.Id });

        var linked = status.Teams.Single();
        Assert.NotNull(linked.LinkedTeamId);
        Assert.Equal("Maccabi", linked.LinkedTeamName);
        Assert.Equal(2, (await TeamsOfAsync(player.Id)).Count); // the school team is kept

        var stranger = await TestUser.SignInAsync(_factory, "stranger");
        var response = await stranger.Http.PostAsync($"/api/ibba/team-links/{ibba[0].Id}/new-team", System.Net.Http.Json.JsonContent.Create(new CreateTeamForIbbaTeamDto { PlayerId = player.Id }));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
