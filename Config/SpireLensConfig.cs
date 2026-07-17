using BaseLib.Config;
using Godot;

namespace SpireLens.Config;

public sealed class SpireLensConfig : SimpleModConfig
{
    [ConfigSection("Deck View")]
    public static bool ViewStatsToggleEnabled { get; set; }

    public static bool ShowRemovedCardsInDeckView { get; set; } = true;

    public static bool ShowEnemyStatsOnHover { get; set; }

    public static bool ShowCardStatsDuringCombat { get; set; }

    [ConfigHideInUI]
    public static bool ShowHandTooltips { get; set; } = true;

    [ConfigSection("Tooltips")]
    public static bool UseVerboseHandStats { get; set; }

    [ConfigSection("Performance")]
    public static bool DisableCardStatsDuringCombat { get; set; }

    [ConfigSection("Diagnostics")]
    public static bool EnableDebugLogging { get; set; }

    [ConfigSection("Build")]
    public static BuildDisplayTimeZone BuildTimeZone { get; set; } = BuildDisplayTimeZone.Pacific;

    [ConfigIgnore]
    public static string BuildVersion => BuildInfo.Version;

    [ConfigIgnore]
    public static string BuildSource => BuildInfo.Source;

    [ConfigIgnore]
    public static string BuildCommit => BuildInfo.CommitHash;

    [ConfigIgnore]
    public static string BuildDate => BuildMetadataFormatter.FormatBuildDate(
        BuildInfo.BuildTimestampUtc,
        BuildTimeZone);

    [ConfigIgnore]
    public static string BuildTimestampUtc => BuildInfo.BuildTimestampUtc;

    [ConfigHideInUI]
    public static bool LegacyPrefsMigrated { get; set; }

    public override void SetupConfigUI(Control root)
    {
        base.SetupConfigUI(root);

        // These property names are legacy persistence keys. Keep them stable
        // on disk, but use the current names anywhere the player sees them.
        RelabelGeneratedOption(root, nameof(ViewStatsToggleEnabled), "SpireLens: on/off");
        RelabelGeneratedOption(root, nameof(ShowCardStatsDuringCombat), "SpireLens: card stats");

        root.AddChild(CreateSectionHeader("Build Info", false));
        root.AddChild(CreateRawLabelControl($"Build version: {BuildVersion}", 18));
        root.AddChild(CreateRawLabelControl($"Build source: {BuildSource}", 18));
        root.AddChild(CreateRawLabelControl($"Commit: {BuildCommit}", 18));
        root.AddChild(CreateRawLabelControl($"Build date: {BuildDate}", 18));
        root.AddChild(CreateRawLabelControl($"Build timestamp UTC: {BuildTimestampUtc}", 18));
    }

    private static void RelabelGeneratedOption(Node root, string optionName, string labelText)
    {
        var row = root.FindChild(optionName, recursive: true, owned: false);
        if (row?.GetChildCount() > 0 && row.GetChild(0) is RichTextLabel label)
            label.Text = labelText;
    }
}
