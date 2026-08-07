using JustyBase.Ai.Chat;

namespace JustyBase.Ai.Tests;

public sealed class ChatMarkdownViewTests
{
    [Fact]
    public void StreamBuffer_UsesFullAppendThenOnlyNewSuffix()
    {
        var buffer = new ChatMarkdownStreamBuffer();
        var clears = 0;
        var appended = new List<string>();

        Assert.True(buffer.Apply("Początek **bold**", () => clears++, appended.Add));
        Assert.Equal(1, clears);
        Assert.Equal(["Początek **bold**"], appended);

        Assert.True(buffer.Apply("Początek **bold** i dalszy tekst", () => clears++, appended.Add));
        Assert.Equal(1, clears);
        Assert.Equal(" i dalszy tekst", appended[^1]);
    }

    [Fact]
    public void StreamBuffer_ResetsWhenSourceIsNoLongerAPrefix()
    {
        var buffer = new ChatMarkdownStreamBuffer();
        var operations = new List<string>();

        buffer.Apply("wersja pierwsza", () => operations.Add("clear"), value => operations.Add($"append:{value}"));
        buffer.Apply("wersja poprawiona", () => operations.Add("clear"), value => operations.Add($"append:{value}"));

        Assert.Equal(["clear", "append:wersja pierwsza", "clear", "append:wersja poprawiona"], operations);
    }

    [Fact]
    public void Sanitizer_PreservesChatMarkdownAndPolishText()
    {
        const string markdown = "ąęłń **pogrubienie** _kursywa_\n- element\n> cytat\n\n| kolumna | wartość |\n| --- | --- |\n| 1 | dwa |\n\n```sql\nselect * from t where a < 2;\n```";

        var sanitized = ChatMarkdownSanitizer.Sanitize(markdown);

        Assert.Equal(markdown, sanitized);
    }

    [Fact]
    public void Sanitizer_RemovesImagesAndUnsafeLinksButKeepsHttpsLinks()
    {
        const string markdown = "![pobierz](https://example.com/image.png) [![obraz w linku](https://example.com/nested.png)](https://example.com/docs) [bezpieczny](https://example.com/docs) [zły](javascript:alert(1)) <script>alert(1)</script>";

        var sanitized = ChatMarkdownSanitizer.Sanitize(markdown);

        Assert.Contains("pobierz", sanitized, StringComparison.Ordinal);
        Assert.Contains("[bezpieczny](https://example.com/docs)", sanitized, StringComparison.Ordinal);
        Assert.DoesNotContain("image.png", sanitized, StringComparison.Ordinal);
        Assert.DoesNotContain("nested.png", sanitized, StringComparison.Ordinal);
        Assert.DoesNotContain("javascript:", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<script>", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("&lt;script&gt;", sanitized, StringComparison.Ordinal);
    }

    [Fact]
    public void Sanitizer_RemovesReferenceImagesAndUnsafeReferenceLinks()
    {
        const string markdown = "![obraz][img] [zły][bad] [dobry][ok]\n\n[img]: https://example.com/image.png\n[bad]: javascript:alert(1)\n[ok]: https://example.com/docs";

        var sanitized = ChatMarkdownSanitizer.Sanitize(markdown);

        Assert.Contains("obraz", sanitized, StringComparison.Ordinal);
        Assert.DoesNotContain("![obraz][img]", sanitized, StringComparison.Ordinal);
        Assert.DoesNotContain("[zły][bad]", sanitized, StringComparison.Ordinal);
        Assert.DoesNotContain("javascript:", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[dobry][ok]", sanitized, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("https://example.com", true)]
    [InlineData("http://localhost:8080/path", true)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("file:///secret.txt", false)]
    [InlineData("data:text/plain,secret", false)]
    [InlineData("//example.com/path", false)]
    public void Sanitizer_AllowsOnlyHttpAndHttpsLinks(string href, bool expected)
    {
        Assert.Equal(expected, ChatMarkdownSanitizer.IsAllowedHttpLink(href));
    }

    [Fact]
    public void StreamBuffer_DoesNotAdvanceStateWhenRendererAppendFails()
    {
        var buffer = new ChatMarkdownStreamBuffer();
        buffer.Apply("old", static () => { }, static _ => { });

        Assert.Throws<InvalidOperationException>(() => buffer.Apply(
            "old new",
            static () => { },
            static _ => throw new InvalidOperationException("renderer failure")));

        Assert.Equal("old", buffer.RenderedText);
    }
}
