namespace QuickRewrite.Models;

public enum RewriteModeKind
{
    Smart,
    Friendly,
    Professional,
    Concise,
    Proofread,
    Custom
}

public sealed record RewriteModeDefinition(
    RewriteModeKind Kind,
    string Title,
    string Subtitle,
    string Glyph)
{
    public bool IsCustom => Kind == RewriteModeKind.Custom;
}

public static class RewriteModes
{
    public static IReadOnlyList<RewriteModeDefinition> All { get; } =
    [
        new(RewriteModeKind.Smart, "智能改写", "改善通顺度、清晰度和整体表达", "✦"),
        new(RewriteModeKind.Friendly, "更友好", "语气更亲切，适合沟通和回复", "☺"),
        new(RewriteModeKind.Professional, "更专业", "更正式、清晰、可信", "◇"),
        new(RewriteModeKind.Concise, "更简洁", "删除冗余，保留事实和重点", "≡"),
        new(RewriteModeKind.Proofread, "校对优化", "只修正错别字、语法和标点", "✓"),
        new(RewriteModeKind.Custom, "自定义指令", "用一句话描述你想要的修改", "✎")
    ];
}