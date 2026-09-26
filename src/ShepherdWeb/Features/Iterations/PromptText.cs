using System.Security.Cryptography;
using System.Text;

namespace ShepherdWeb.Features.Iterations;

/// <summary>
/// Facts about prompt text as it would be stored and delivered: UTF-8 without a BOM, line endings
/// exactly as typed (docs/ARCHITECTURE.md AD-8). The SHA-256 here is the same hash a delivered-prompt
/// artifact will carry, so the operator sees the identity of the text while authoring it.
/// </summary>
public sealed class PromptText
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public PromptText(string? text)
    {
        Text = text ?? string.Empty;
        var bytes = Utf8NoBom.GetBytes(Text);
        ByteCount = bytes.Length;
        Sha256 = Convert.ToHexString(SHA256.HashData(bytes)); // Upper-case hex, as ShepherdAPI stores SHA256.
        LineCount = Text.Length == 0 ? 0 : Text.Count(c => c == '\n') + 1;
    }

    public string Text { get; }
    public int CharacterCount => Text.Length;
    public int LineCount { get; }
    public int ByteCount { get; }
    public string Sha256 { get; }
}
