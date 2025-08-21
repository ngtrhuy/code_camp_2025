public class SelectionSpec
{
    public string? Css { get; set; }
    public string? XPath { get; set; }
    public string? TextHint { get; set; } // fallback: tìm node theo text
}

public class AncestorSpec
{
    public bool Auto { get; set; } = true;
    public string? Css { get; set; }
    public string? XPath { get; set; }
}

public class ResolveSelectionRequest
{
    public string Url { get; set; } = "";
    public string Mode { get; set; } = "server_side"; // server_side | client_side | auto
    public string? LoadMoreSelector { get; set; }
    public int LoadMoreClicks { get; set; } = 0;

    public SelectionSpec Selection { get; set; } = new();
    public AncestorSpec Ancestor { get; set; } = new();
    public int SampleLimit { get; set; } = 20;
}

public class ResolveSelectionResponse
{
    public string ItemAncestorXPath { get; set; } = "";
    public string FieldRelativeXPath { get; set; } = "";
    public object AttrSuggestion { get; set; } = new { imageAttr = (string?)null, linkAttr = (string?)null };
    public int ItemsMatched { get; set; }
    public double FieldCoveragePct { get; set; }
    public List<string> Samples { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}
