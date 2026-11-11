namespace Ps2Ulix.Core.Tests;

public sealed class UlDriveMaintenanceTests : DriveTestBase
{
    private (UlGameEntry Ico, UlGameEntry Okami) AddTwo() =>
        (_drive.AddGame(MakeIso("Ico.iso", "SCUS_971.13")), _drive.AddGame(MakeIso("Okami.iso", "SLES_544.39", 30_000)));

    private DriveIssue SingleIssue(IssueSeverity severity, string text)
    {
        var issues = _drive.Check().Issues;
        return Assert.Single(issues, i => i.Severity == severity && i.Message.Contains(text, StringComparison.Ordinal));
    }

    [Fact]
    public void Check_HealthyDrive_NoIssues()
    {
        var (ico, okami) = AddTwo();

        var report = _drive.Check();

        Assert.Empty(report.Issues);
        Assert.False(report.HasErrors);
        Assert.Equal([ico, okami], report.Games);
    }

    [Fact]
    public void Check_MissingPart_IsError()
    {
        var (_, okami) = AddTwo();
        File.Delete(_drive.GetPartPath(okami, 1));

        SingleIssue(IssueSeverity.Error, "missing part(s) 01");
        Assert.True(_drive.Check().HasErrors);
    }

    [Fact]
    public void Check_WrongPartSize_IsError()
    {
        var (_, okami) = AddTwo();
        using (var f = File.OpenWrite(_drive.GetPartPath(okami, 0)))
            f.SetLength(PartSize - 1);

        SingleIssue(IssueSeverity.Error, "part 00 is");
    }

    [Fact]
    public void Check_OrphanParts_IsWarning()
    {
        var (ico, okami) = AddTwo();
        UlCfg.Save(_drive.CfgPath, [ico]);

        SingleIssue(IssueSeverity.Warning, $"orphan part file(s) ul.{okami.NameCrc:X8}.SLES_544.39.*");
    }

    [Fact]
    public void Check_InvalidRecordAndTrailingBytes()
    {
        AddTwo();
        using (var f = new FileStream(_drive.CfgPath, FileMode.Append))
            f.Write(new byte[UlGameEntry.Size + 5]);

        SingleIssue(IssueSeverity.Error, "record #3 is invalid");
        SingleIssue(IssueSeverity.Warning, "5 stray bytes");
    }

    [Fact]
    public void Check_DiscIdDiffersFromCfg_IsWarning()
    {
        var real = _drive.AddGame(MakeIso("Ico.iso", "SCUS_971.13"));
        var wrong = new UlGameEntry(real.Name, "SLUS_999.99", real.Parts, real.Media);
        for (var p = 0; p < real.Parts; p++)
            File.Move(_drive.GetPartPath(real, p), _drive.GetPartPath(wrong, p));
        UlCfg.Save(_drive.CfgPath, [wrong]);

        SingleIssue(IssueSeverity.Warning, "disc boots SCUS_971.13, ul.cfg says SLUS_999.99");
    }

    [Fact]
    public void Check_PartsWithoutCfg_IsError()
    {
        AddTwo();
        File.Delete(_drive.CfgPath);

        SingleIssue(IssueSeverity.Error, "No ul.cfg");
    }

    [Fact]
    public void Check_UnrecognizedUlFile_IsWarning()
    {
        AddTwo();
        File.WriteAllText(Path.Combine(_driveDir, "ul.junk"), "x");

        SingleIssue(IssueSeverity.Warning, "Unrecognized file ul.junk");
    }

    [Fact]
    public void Rebuild_NoCfg_RecoversGamesNamedById()
    {
        var okamiIso = MakeIso("Okami.iso", "SLES_544.39", 30_000);
        var (ico, okami) = AddTwo();
        File.Delete(_drive.CfgPath);

        var result = _drive.Rebuild();

        var expected = new[]
        {
            new UlGameEntry("SCUS_971.13", "SCUS_971.13", ico.Parts, UlMedia.Cd),
            new UlGameEntry("SLES_544.39", "SLES_544.39", okami.Parts, UlMedia.Cd),
        };
        Assert.Equal(expected, result.Games);
        Assert.Equal(expected, result.Recovered);
        Assert.Equal(expected, _drive.LoadGames());
        Assert.Empty(_drive.Check().Issues);
        Assert.False(File.Exists(Path.Combine(_driveDir, UlDrive.BackupFileName)));

        var output = Path.Combine(_work, "out.iso");
        _drive.JoinGame(expected[1], output);
        Assert.Equal(File.ReadAllBytes(okamiIso), File.ReadAllBytes(output));
    }

    [Fact]
    public void Rebuild_KeepsListed_DropsMissing_RecoversOrphans_BacksUp()
    {
        var (ico, okami) = AddTwo();
        var ghost = new UlGameEntry("Ghost", "SLUS_000.01", 1, UlMedia.Dvd);
        UlCfg.Save(_drive.CfgPath, [ghost, okami]);
        var oldCfg = File.ReadAllBytes(_drive.CfgPath);

        var result = _drive.Rebuild();

        var recoveredIco = new UlGameEntry("SCUS_971.13", "SCUS_971.13", ico.Parts, UlMedia.Cd);
        Assert.Equal([okami, recoveredIco], _drive.LoadGames());
        Assert.Equal([recoveredIco], result.Recovered);
        Assert.Equal([ghost], result.Dropped);
        Assert.Equal(oldCfg, File.ReadAllBytes(Path.Combine(_driveDir, UlDrive.BackupFileName)));
        Assert.Empty(_drive.Check().Issues);
    }

    [Fact]
    public void Rebuild_SameIdOrphans_GetUniqueNames()
    {
        _drive.AddGame(MakeIso("Okami.iso"));
        _drive.AddGame(MakeIso("Okami Copy.iso"));
        File.Delete(_drive.CfgPath);

        var result = _drive.Rebuild();

        Assert.Equal(["SLUS_209.46", "SLUS_209.46 (2)"], result.Games.Select(g => g.Name).Order());
        Assert.Empty(_drive.Check().Issues);
    }

    [Fact]
    public void Rebuild_IncompleteSet_ReportedAndLeftOnDisk()
    {
        var (ico, okami) = AddTwo();
        File.Delete(_drive.GetPartPath(okami, 1));
        var before = PartFiles();

        var result = _drive.Rebuild();

        Assert.Equal([ico], result.Games);
        Assert.Contains("missing 01", Assert.Single(result.Incomplete));
        Assert.Equal(before, PartFiles());
    }

    [Fact]
    public void Rebuild_DryRun_ChangesNothing()
    {
        AddTwo();
        File.Delete(_drive.CfgPath);
        var before = PartFiles();

        var result = _drive.Rebuild(dryRun: true);

        Assert.Equal(2, result.Recovered.Count);
        Assert.Equal(before, PartFiles());
        Assert.False(File.Exists(_drive.CfgPath));
    }

    [Fact]
    public void Rebuild_CorruptRecord_KeepsValidOnes()
    {
        var (ico, okami) = AddTwo();
        var bytes = File.ReadAllBytes(_drive.CfgPath);
        Array.Clear(bytes, 0, UlGameEntry.Size); // wipe Ico's record
        File.WriteAllBytes(_drive.CfgPath, bytes);

        var result = _drive.Rebuild();

        Assert.Equal(okami, result.Games[0]);
        Assert.Equal("SCUS_971.13", Assert.Single(result.Recovered).Name);
        Assert.Equal(ico.Parts, result.Recovered[0].Parts);
    }
}
