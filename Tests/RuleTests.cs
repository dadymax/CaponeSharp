using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Capone.Tests;

/// <summary>Line of sight (Vision.Build, sub_447660 / sub_4471D0 / sub_4470D0) on small hand-made maps.</summary>
public static class VisionTests
{
    private static void Field(params Vector2I[] opaque)
    {
        var used = new List<Vector2I>();
        for (var y = -10; y <= 10; y++)
            for (var x = -10; x <= 10; x++)
                used.Add(new Vector2I(x, y));
        Vision.InitForTest(used, opaque);
    }

    private static bool Sees(int r, int x, int y) => Vision.Visible(Vision.Build(Vector2I.Zero, r), Vector2I.Zero, r, new Vector2I(x, y));

    [UnitTest]
    public static void OpenFieldSeesEverythingWithinRadius()
    {
        Field();
        // Radius 5 has no unrolled copy: the generic loop only tests the steps back.
        for (var y = -4; y <= 4; y++)
            for (var x = -4; x <= 4; x++)
                Assert.True(Sees(5, x, y), $"({x}, {y}) visible at radius 5");
        Assert.True(Sees(5, 5, 4), "(5, 4): the step back (4, 3) is within 5");
        Assert.False(Sees(5, 5, 5), "(5, 5): the step back (4, 4) is 5.66 away, beyond 5");
        Assert.False(Sees(5, 6, 0), "outside the grid");
    }

    [UnitTest]
    public static void UnrolledRadiiLoseTheOuterRing()
    {
        Field();
        // sub_456D98 / sub_457D79 / sub_44C237 / sub_453BF1 clear ring r and cells farther than r.
        foreach (var r in new[] { 3, 4, 8, 9 })
        {
            Assert.True(Sees(r, r - 1, 0), $"r {r}: ({r - 1}, 0)");
            Assert.False(Sees(r, r, 0), $"r {r}: ({r}, 0) is on the outer ring");
            Assert.False(Sees(r, 0, -r), $"r {r}: (0, {-r}) is on the outer ring");
        }
        Assert.True(Sees(4, 3, 2), "r 4: (3, 2) is 3.6 away");
        Assert.False(Sees(4, 3, 3), "r 4: (3, 3) is 4.24 away");
        Assert.True(Sees(8, 3, 7), "r 8: (3, 7) is 7.6 away");
        Assert.False(Sees(8, 4, 7), "r 8: (4, 7) is 8.06 away");
        Assert.False(Sees(9, 6, 7), "r 9: (6, 7) is 9.22 away");
        Assert.True(Sees(9, 5, 7), "r 9: (5, 7) is 8.6 away");
    }

    [UnitTest]
    public static void WallIsSeenButNotWhatIsBehind()
    {
        Field(new Vector2I(1, 0));
        Assert.True(Sees(4, 1, 0), "the wall itself (ring 1 is always seen)");
        Assert.False(Sees(4, 2, 0), "right behind the wall");
        Assert.False(Sees(4, 3, 0), "further behind the wall");
        Assert.True(Sees(4, 2, 1), "(2, 1): the diagonal step back (1, 1) is open");
        Assert.True(Sees(4, 2, -1), "(2, -1): the diagonal step back (1, -1) is open");
    }

    [UnitTest]
    public static void LongWallHidesTheSector()
    {
        Field(new Vector2I(1, -1), new Vector2I(1, 0), new Vector2I(1, 1));
        Assert.False(Sees(4, 2, 0), "(2, 0)");
        Assert.False(Sees(4, 2, 1), "(2, 1): both steps back are walls");
        Assert.False(Sees(4, 2, 2), "(2, 2): diagonal step back is a wall");
        Assert.True(Sees(4, 1, 2), "(1, 2): y dominates, the step back (0, 1) is open");
        Assert.True(Sees(4, -3, 0), "the other side is open");
    }

    [UnitTest]
    public static void CellsOffTheMapBlockSight()
    {
        var used = new List<Vector2I>();
        for (var x = -10; x <= 0; x++)
            used.Add(new Vector2I(x, 0));
        Vision.InitForTest(used, System.Array.Empty<Vector2I>());
        Assert.True(Sees(4, -3, 0), "along the strip");
        Assert.False(Sees(4, 2, 0), "beyond the map edge");
    }

    [UnitTest]
    public static void SniperSeesFurtherStandingStill()
    {
        var sniper = OriginalUnitTable.Instance.Get((int)UnitCommon.UnitType.Sniper);
        Assert.Equal(8, sniper.Vision, "Sniper vision while walking (+2Ch)");
        Assert.Equal(9, sniper.VisionMax, "Sniper vision standing (+2Ah)");
    }
}

/// <summary>Cash, overdraft, deposits and bribes (Organisation, sub_430860 / sub_430790 / sub_430C30).</summary>
public static class EconomyTests
{
    private static Organisation NewOrg(long funds) => new(new Team(Colors.Red) { PlayerName = "test" }, funds);

    [UnitTest]
    public static void AllianceSquaresFollowTheExe()
    {
        // sub_45B796: row = their stance (vt+0E0h, 1 not cooperating / 2 cooperating), column = ours.
        var me = new Team(Colors.Red) { PlayerName = "me" };
        var ai = new Team(Colors.Blue) { PlayerName = "ai" };
        Assert.Equal(0, AllianceWindow.SquareOf(me, ai), "nobody cooperates by default: top left");
        me.SetTargetRelation(ai.TeamColor, Teams.TeamRelation.Friend);
        Assert.Equal(1, AllianceWindow.SquareOf(me, ai), "only we: top right");
        ai.SetTargetRelation(me.TeamColor, Teams.TeamRelation.Friend);
        Assert.Equal(3, AllianceWindow.SquareOf(me, ai), "both: bottom right");
        ai.Org = new Organisation(ai, 0);
        AllianceWindow.Apply(me, new[] { (ai, false) });
        Assert.Equal(Teams.TeamRelation.Enemy, me.GetTargetRelation(ai.TeamColor), "Изменить sets our stance");
        Assert.Equal(Teams.TeamRelation.Friend, ai.GetTargetRelation(me.TeamColor), "theirs stays");
    }

    [UnitTest]
    public static void UnitHostilityRules()
    {
        var me = new Team(Colors.Red) { PlayerName = "me" };
        var ai = new Team(Colors.Blue) { PlayerName = "ai" };
        Assert.True(Unit.Hostile(me, false, false, ai, false, false), "players are enemies by default (relation 1)");
        Assert.True(!Unit.Hostile(me, false, false, me, false, false), "own units are not");
        me.SetTargetRelation(ai.TeamColor, Teams.TeamRelation.Friend);
        Assert.True(!Unit.Hostile(me, false, false, ai, false, false), "our stance: cooperating");
        Assert.True(Unit.Hostile(ai, false, false, me, false, false), "theirs is their own: still hostile");
        Assert.True(Unit.Hostile(null, true, false, me, false, false), "a deserter fights owned units");
        Assert.True(Unit.Hostile(me, false, false, null, true, false), "owned units fight deserters");
        Assert.True(!Unit.Hostile(null, true, false, null, false, false), "deserters leave unowned units alone");
        Assert.True(Unit.Hostile(null, false, true, null, false, true), "rogues fight everyone");
    }

    [UnitTest]
    public static void DisguiseTiming()
    {
        const int h = GameClock.MinutesPerHour;
        long shot = -100 * h, start = 1000 * h;
        Assert.True(!Unit.Exposed(false, shot, start, start), "just disguised: hidden");
        Assert.True(!Unit.Exposed(false, shot, start, start + 60 * h - 1), "hidden for 60 hours");
        Assert.True(Unit.Exposed(false, shot, start, start + 60 * h), "exposed after 60 hours");
        Assert.True(Unit.Exposed(false, start + 5 * h, start, start + 35 * h), "a shot exposes for 30 hours (inclusive)");
        Assert.True(!Unit.Exposed(false, start + 5 * h, start, start + 35 * h + 1), "then hidden again");
        Assert.True(!Unit.Exposed(true, shot, -1, 0), "the Hit man hides by himself");
        Assert.True(Unit.Exposed(true, 0, -1, 30 * h - 1), "exposed after a shot");
        Assert.True(!Unit.Exposed(true, 0, -1, 30 * h), "for 30 hours (strictly)");
    }

    [UnitTest]
    public static void OrderModes()
    {
        // sub_42C1F6 actions 0 / 5 (Go / Force Go) and 1 / 6 (Attack / Force Attack) → sub_4284B0 mode.
        Assert.Equal(2, Unit.GoMode(UnitCommon.UnitType.Pistol, false), "Go fights on the way");
        Assert.Equal(0, Unit.GoMode(UnitCommon.UnitType.Pistol, true), "Force Go never fights");
        Assert.Equal(0, Unit.GoMode(UnitCommon.UnitType.Terrorist, false), "the Terrorist always walks");
        Assert.Equal(0, Unit.GoMode(UnitCommon.UnitType.Thief, false), "the Thief always walks");
        Assert.Equal(3, Unit.AttackMode(UnitCommon.UnitType.Thompson, false), "Attack: the target, else the nearest enemy");
        Assert.Equal(1, Unit.AttackMode(UnitCommon.UnitType.Thompson, true), "Force Attack: only the target");
        Assert.Equal(1, Unit.AttackMode(UnitCommon.UnitType.Terrorist, false), "the Terrorist goes for his target only");
    }

    [UnitTest]
    public static void PaydayFollowsTheExe()
    {
        // sub_42A535: a day's pay = weekly salary / 7, paid while cash − pay + 200 000 ≥ 0.
        var org = NewOrg(0);
        Assert.Equal(Unit.PayResult.Paid, Unit.Pay(org, 70_000, UnitCommon.UnitType.Pistol), "within the overdraft");
        Assert.Equal(-10_000L, org.Cash, "a day's pay");
        var broke = NewOrg(-195_000);
        Assert.Equal(Unit.PayResult.Quits, Unit.Pay(broke, 70_000, UnitCommon.UnitType.Pistol), "beyond: the gangster quits");
        Assert.Equal(Unit.PayResult.Unpaid, Unit.Pay(broke, 70_000, UnitCommon.UnitType.Collector), "a Collector stays unpaid");
        Assert.Equal(-195_000L, broke.Cash, "nothing paid");
        Assert.Equal(Unit.PayResult.Paid, Unit.Pay(broke, 0, UnitCommon.UnitType.Thug), "no salary (episode flag A = 0)");
    }

    [UnitTest]
    public static void MoneyTransferFollowsTheExe()
    {
        // sub_462C13: amount = 50 000 × (slider + 1), slider 0..19; sub_4294B0: paid only when affordable (vt+70h).
        Assert.Equal(50_000L, MoneyWindow.AmountOf(0), "lowest amount");
        Assert.Equal(1_000_000L, MoneyWindow.AmountOf(MoneyWindow.Positions - 1), "highest amount");
        var giver = NewOrg(300_000);
        var receiver = NewOrg(100_000);
        giver.Team.Org = giver;
        receiver.Team.Org = receiver;
        Assert.True(!MoneyWindow.Transfer(giver.Team, receiver.Team, 600_000), "beyond the 200 000 overdraft: refused");
        Assert.Equal(300_000L, giver.Cash, "nothing paid when refused");
        Assert.True(MoneyWindow.Transfer(giver.Team, receiver.Team, 450_000), "within the overdraft: paid");
        Assert.Equal(-150_000L, giver.Cash, "giver pays");
        Assert.Equal(550_000L, receiver.Cash, "receiver deposits");
    }

    [UnitTest]
    public static void MarketShareGrowsWithIncomeAndRivalsCapIt()
    {
        // sub_430C30 / sub_430BE8: share = own income / max(all players' income of the type, 5 000 000).
        Assert.True(Organisation.ShareOf(1_000_000, 1_000_000) < Organisation.ShareOf(3_000_000, 3_000_000),
            "a small market alone stays under the floor; more income, more share");
        Assert.Equal(1f, Organisation.ShareOf(6_000_000, 6_000_000), "alone above the floor: 100 %");
        Assert.True(Organisation.ShareOf(6_000_000, 6_000_001) < 1f, "a rival earning anything keeps it under 100 %");
        Assert.True(Organisation.ShareOf(4_000_000, 6_000_000) > Organisation.ShareOf(2_000_000, 6_000_000),
            "the pricier (higher-income) business takes the bigger share");
        Assert.True(Organisation.MarketFactor(0) == 1f && Organisation.MarketFactor(100) > Organisation.MarketFactor(50)
            && Organisation.MarketFactor(50) > Organisation.MarketFactor(10), "the income bonus grows with the share");
    }

    [UnitTest]
    public static void SoundTableMatchesTheExe()
    {
        // off_478700: sub_408A92 plays by index.
        Assert.Equal("Click.wav", Sound.FileOf(0), "id 0");
        Assert.Equal("OutOfFunds.wav", Sound.FileOf(0x18), "id 18h (Spend refused)");
        Assert.Equal("ThugIdle1.wav", Sound.FileOf(0xC5), "id C5h (Thug idle)");
        Assert.Equal("SniperIdle5.wav", Sound.FileOf(0xA5), "id A5h");
        Assert.True(Sound.FileOf(0x19) == null, "id 19h is an empty slot");
    }

    [UnitTest]
    public static void RankPlacesFollowTheExe()
    {
        // sub_45FD50: H − (value − min) × H / (max − min), H = 375 − 135; equal players → 120 (the middle).
        var me = new long[] { 50_000, 1_000, 4, 2, 0 };
        var rival = new long[] { 150_000, 1_000, 0, 6, 0 };
        var places = RankWindow.Places(me, new[] { me, rival });
        Assert.Equal(RankWindow.Height, places[0], "the poorest is at the bottom");
        Assert.Equal(RankWindow.Middle, places[1], "equal incomes: the middle");
        Assert.Equal(0, places[2], "the most gangsters: the top");
        Assert.Equal(RankWindow.Height, places[3], "fewest companies: the bottom");
        Assert.Equal(RankWindow.Middle, places[4], "nobody has businesses: the middle");
        var third = new long[] { 100_000, 1_000, 2, 4, 0 };
        Assert.Equal(120, RankWindow.Places(third, new[] { me, rival, third })[0], "half way: the middle");
        Assert.True(RankWindow.ColumnX.SequenceEqual(new[] { 140, 258, 376, 496, 602 }), "columns (table 0x48E4EC)");
    }

    [UnitTest]
    public static void CursorsComeFromTheExeResources()
    {
        // fnLegalCrimeMainWindowMain: LoadCursor ArrowCur / GotoCur / AttackCu / ProtectCu (PE cursor groups).
        var exe = OriginalExe.Instance;
        foreach (var (name, hotspot) in new[] { ("ARROWCUR", new Vector2(1, 1)), ("GOTOCUR", new Vector2(1, 1)),
                     ("ATTACKCU", new Vector2(10, 10)), ("PROTECTCU", new Vector2(1, 1)) })
        {
            var group = exe.ReadResource(12, name);
            Assert.True(group != null, $"{name}: group");
            var data = exe.ReadResource(1, null, System.BitConverter.ToUInt16(group, 18));
            var cursor = OriginalCursors.Decode(data);
            Assert.True(cursor is { } c && c.Item1.GetWidth() == 32 && c.Item1.GetHeight() == 32 && c.Item2 == hotspot,
                $"{name}: {cursor?.Item1.GetSize()} hotspot {cursor?.Item2}");
        }
    }

    [UnitTest]
    public static void CompanySignsAreChicagoTiles()
    {
        // sub_422276: every sign in companies.json is a Chicago.tile picture (0DC8h, 70Dh–711h, 9D3h, 0D87h–0DACh).
        var json = Json.ParseString(FileAccess.GetFileAsString("res://Buildings/companies.json")).AsGodotDictionary();
        foreach (var (type, entry) in json)
        {
            var sprites = entry.AsGodotDictionary();
            if (!sprites.ContainsKey("Sprites"))
                continue;
            foreach (var (owner, name) in sprites["Sprites"].AsGodotDictionary())
                Assert.True(OriginalData.Instance.Tiles.ResolveTile(name.AsString()) != null, $"{type}/{owner}: '{name}'");
        }
    }

    [UnitTest]
    public static void BuildingPortraitsAreBlockInfPictures()
    {
        // sub_422158 / sub_415258 / sub_429D30: every portrait is one of BLOCKINF.BMP's 84 x 95 "info" tiles.
        foreach (var name in new[] { "info slum", "info theathern", "info L-house", "info news stand", "info hall",
                     "info hotel 2", "info hotel 1", "info central 1", "info extra 1", "info police", "HQ info picture" })
        {
            var tile = OriginalData.Instance.Tiles.ResolveTile(name);
            Assert.True(tile != null && tile.Size == new Vector2I(84, 95), $"{name}: {tile?.Size}");
        }
    }

    [UnitTest]
    public static void FavourBarFollowsTheSquareRoot()
    {
        // sub_40A245: sqrt(points) / 40 x 112 px, then (points - 1600)^(1/4) more, at most 20.
        Assert.Equal(0, SharesPanel.FavourBarWidth(0), "no points");
        Assert.Equal(28, SharesPanel.FavourBarWidth(100), "100 points: 10 / 40 of 112");
        Assert.Equal(112, SharesPanel.FavourBarWidth(1600), "1600 points: full");
        Assert.Equal(115, SharesPanel.FavourBarWidth(1700), "1700 points: 100^(1/4) = 3.2 more");
        Assert.Equal(132, SharesPanel.FavourBarWidth(1_000_000), "capped 20 past full");
    }

    [UnitTest]
    public static void OverdraftLimitIs200000()
    {
        var org = NewOrg(0);
        Assert.True(org.CanAfford(200_000), "200 000 into debt is allowed");
        Assert.False(org.CanAfford(200_001), "one more is not");
    }

    [UnitTest]
    public static void SpendingPastTheOverdraftIsAllowedWhileNetIncomeIsNotNegative()
    {
        var org = NewOrg(0);
        org.SetIncomeForTest(0, 0);
        Assert.True(org.Spend(500_000), "paid");
        Assert.Equal(-500_000L, org.Cash, "cash");
        Assert.Equal(500_000L, org.TotalOut, "ledger out");
    }

    [UnitTest]
    public static void DepositWithoutProfitKeepsEverything()
    {
        var org = NewOrg(1000);
        org.Shares[0] = 0;
        org.Shares[1] = 100;
        org.SetIncomeForTest(-10, 100);
        Assert.Equal(400L, org.Deposit(400), "kept");
        Assert.Equal(1400L, org.Cash, "cash");
        Assert.Equal(0L, org.AuthorityMoney[1], "no bribe");
    }

    [UnitTest]
    public static void DepositSplitsTheProfitByShares()
    {
        var org = NewOrg(0);
        float[] shares = { 60, 10, 10, 10, 10 };
        shares.CopyTo(org.Shares, 0);
        org.SetIncomeForTest(500, 1000); // profit part = half of each deposit
        var kept = org.Deposit(1000);
        Assert.Equal(800L, kept, "kept: 500 not profit + 300 saved profit");
        for (var i = 1; i < Organisation.TypeCount; i++)
        {
            Assert.Equal(50L, org.AuthorityMoney[i], $"bribe to authority {i}");
            Assert.Near(50.0 / Organisation.FavourPointPrice, org.Favour[i], $"favour with authority {i}");
        }
        Assert.Equal(org.StartCash + org.TotalIn - org.TotalOut, org.Cash, "ledger");
        Assert.Equal(200L, org.TotalBribed, "bribed");
    }

    [UnitTest]
    public static void ServiceCostsFavourPointsNotMoney()
    {
        var org = NewOrg(0);
        var item = AuthorityItems.Get(0x26);
        Assert.True(item != null, "service 26h exists");
        Assert.False(org.CanUseItem(0x26), "nothing bribed yet");
        org.AuthorityMoney[item.Authority] = item.Price;
        org.Favour[item.Authority] = item.Points + 1;
        Assert.True(org.UseItem(0x26), "used");
        Assert.Near(1, org.Favour[item.Authority], "points paid");
        Assert.Equal(item.Price, org.AuthorityMoney[item.Authority], "money stays");
    }

    [UnitTest]
    public static void MarketShareFactorSteps()
    {
        Assert.Near(1.0, Organisation.MarketFactor(0), "0 %");
        Assert.Near(1.0, Organisation.MarketFactor(9), "9 %");
        Assert.Near(1.11, Organisation.MarketFactor(10), "10 %");
        Assert.Near(1.56, Organisation.MarketFactor(55), "55 %");
        Assert.Near(2.25, Organisation.MarketFactor(100), "100 %");
        Assert.Near(2.25, Organisation.MarketFactor(150), "past 100 %");
    }

    [UnitTest]
    public static void LegalizationLowersIncome()
    {
        float[] expected = { 1.0f, 0.75f, 0.5f, 0.25f };
        for (var i = 0; i < expected.Length; i++)
            Assert.Near(expected[i], Organisation.LegalizationFactor[i], $"level {i}");
    }
}

/// <summary>Extortion, business tiers, licences and the services used on other players' companies.</summary>
public static class BusinessTests
{
    [UnitTest]
    public static void ExtortionChanceFallsWithIncome()
    {
        Assert.Equal(100, Company.IncomeChance(0), "no income");
        Assert.Equal(90, Company.IncomeChance(25_000), "25 000");
        Assert.Equal(52, Company.IncomeChance(62_500), "62 500 (linear)");
        Assert.Equal(15, Company.IncomeChance(100_000), "100 000");
        Assert.Equal(7, Company.IncomeChance(550_000), "550 000 (linear)");
        Assert.Equal(0, Company.IncomeChance(1_000_000), "1 000 000");
        Assert.Equal(0, Company.IncomeChance(5_000_000), "5 000 000");
    }

    private static OriginalBusinessTable.Entry Row(int district, int price) => new() { Value24 = district, Price = price };

    [UnitTest]
    public static void PriceTiersByDistrict()
    {
        Assert.Equal(0, BusinessRules.Tier(Row(0, 99_999)), "slum, cheap");
        Assert.Equal(1, BusinessRules.Tier(Row(0, 100_000)), "slum, 100 000");
        Assert.Equal(2, BusinessRules.Tier(Row(0, 200_000)), "slum, 200 000");
        Assert.Equal(0, BusinessRules.Tier(Row(1, 700_000)), "middle class, 700 000");
        Assert.Equal(1, BusinessRules.Tier(Row(1, 750_000)), "middle class, 750 000");
        Assert.Equal(2, BusinessRules.Tier(Row(2, 2_000_000)), "center, 2 000 000");
    }

    [UnitTest]
    public static void LicencesByDistrictAndTier()
    {
        Assert.Equal(-1, BusinessRules.LicenceItem(Row(0, 50_000)), "cheap slum business needs none");
        Assert.Equal(0x23, BusinessRules.LicenceItem(Row(0, 150_000)), "slum tier 1");
        Assert.Equal(0x1F, BusinessRules.LicenceItem(Row(2, 2_500_000)), "dearest center business");
        Assert.Equal(-1, BusinessRules.LicenceItem(Row(3, 2_500_000)), "any-district rows");
    }

    [UnitTest]
    public static void RaidUsesPoliceServiceByDistrictAndTier()
    {
        int[,] expected = { { 0x26, 0x2B, 0x2F }, { 0x32, 0x35, -1 }, { -1, -1, -1 } };
        for (var d = 0; d < 3; d++)
            for (var t = 0; t < 3; t++)
            {
                var s = CompanyServices.RaidServiceFor(d, t);
                Assert.Equal(expected[d, t], s, $"district {d} tier {t}");
                if (s >= 0)
                    Assert.Equal(4, AuthorityItems.Get(s).Authority, $"service {s:X}h belongs to the police");
            }
    }

    [UnitTest]
    public static void DemolitionIsForSlumsOnlyAndUsesThePolitician()
    {
        int[] expected = { 0x14, 0x18, 0x1D };
        for (var t = 0; t < 3; t++)
        {
            Assert.Equal(expected[t], CompanyServices.DemolishServiceFor(0, t), $"slum tier {t}");
            Assert.Equal(3, AuthorityItems.Get(expected[t]).Authority, $"service {expected[t]:X}h belongs to the politician");
            Assert.Equal(-1, CompanyServices.DemolishServiceFor(1, t), $"middle class tier {t}");
        }
    }

    [UnitTest]
    public static void BrownoutTriesTheLongestCutFirst()
    {
        var list = CompanyServices.Brownouts.ToList();
        Assert.Equal(3, list.Count, "three services");
        Assert.Equal((0x1C, 2L * GameClock.MinutesPerWeek), list[0], "two weeks");
        Assert.Equal((0x19, (long)GameClock.MinutesPerWeek), list[1], "one week");
        Assert.Equal((0x15, 5040L), list[2], "3.5 days");
        foreach (var (service, _) in list)
            Assert.Equal(3, AuthorityItems.Get(service).Authority, $"service {service:X}h belongs to the politician");
    }
}

/// <summary>The game clock's event queue (World::Schedule sub_43AF10, run in time order by sub_43ABD0).</summary>
public static class ClockTests
{
    [UnitTest]
    public static void EventsRunInTimeOrderAndSameMinuteInOrderGiven()
    {
        var clock = new GameClock();
        try
        {
            var log = new List<string>();
            clock.ScheduleAt(30, () => log.Add("30"));
            clock.ScheduleAt(10, () => log.Add("10a"));
            clock.ScheduleAt(70, () => log.Add("70"));
            clock.ScheduleAt(10, () => log.Add("10b"));
            clock._Process(1.0); // one real second = 60 game minutes at speed 1
            Assert.Equal("10a,10b,30", string.Join(",", log), "first hour");
            clock._Process(1.0);
            Assert.Equal("10a,10b,30,70", string.Join(",", log), "second hour");
            clock.ScheduleAt(5, () => log.Add("past"));
            clock._Process(0.001);
            Assert.Equal("past", log[^1], "an event in the past runs at once");
        }
        finally
        {
            clock.Free();
        }
    }

    [UnitTest]
    public static void AFailingEventIsCountedAndTheOthersStillRun()
    {
        var clock = new GameClock();
        try
        {
            var errors = GameClock.EventErrors;
            var ran = false;
            clock.ScheduleAt(1, () => throw new System.InvalidOperationException("test: expected failure"));
            clock.ScheduleAt(2, () => ran = true);
            clock._Process(1.0);
            Assert.Equal(errors + 1, GameClock.EventErrors, "error counted");
            Assert.True(ran, "the next event ran");
        }
        finally
        {
            clock.Free();
        }
    }
}

/// <summary>Number and printf formatting of the original texts (sub_408AD0, the C runtime's sprintf).</summary>
public static class TextTests
{
    [UnitTest]
    public static void MoneyGroupsDigitsBySpaces()
    {
        Assert.Equal("0", PanelInfo.Money(0), "0");
        Assert.Equal("999", PanelInfo.Money(999), "999");
        Assert.Equal("1 000", PanelInfo.Money(1000), "1000");
        Assert.Equal("1 234 567", PanelInfo.Money(1_234_567), "1234567");
        Assert.Equal("-200 000", PanelInfo.Money(-200_000), "-200000");
    }

    [UnitTest]
    public static void CFormatHandlesTheOriginalSpecifiers()
    {
        Assert.Equal("Jim has 5 men", PanelInfo.CFormat("%s has %d men", "Jim", 5), "%s %d");
        Assert.Equal("cost 12", PanelInfo.CFormat("cost %ld", 12L), "%ld");
        Assert.Equal("1.5 pts", PanelInfo.CFormat("%.1f pts", 1.5), "%.1f");
        Assert.Equal("50% off", PanelInfo.CFormat("%d%% off", 50), "%%");
        Assert.Equal("x  y", PanelInfo.CFormat("x %s y"), "missing argument");
    }
}
