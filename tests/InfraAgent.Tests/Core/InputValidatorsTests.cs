using InfraAgent.Core.Settings;
using InfraAgent.Core.Validation;

namespace InfraAgent.Tests.Core;

public class InputValidatorsTests
{
    [Theory]
    [InlineData("main.tf", "main.tf")]
    [InlineData("/modules/net/main.tf", "modules/net/main.tf")]
    [InlineData("modules\\net\\main.tf", "modules/net/main.tf")]
    public void RelativePath_normalizes_valid_paths(string input, string expected) =>
        Assert.Equal(expected, InputValidators.RelativePath(input));

    [Theory]
    [InlineData("../secrets.tf")]
    [InlineData("a/../../b.tf")]
    [InlineData("C:/windows/x.tf")]
    [InlineData("a//b.tf")]
    [InlineData("")]
    [InlineData("a\0b.tf")]
    public void RelativePath_rejects_traversal_drives_and_empty_segments(string input) =>
        Assert.Throws<ArgumentException>(() => InputValidators.RelativePath(input));

    [Theory]
    [InlineData("main.tf")]
    [InlineData("vars/prod.tfvars")]
    [InlineData("README.md")]
    public void GeneratedFilePath_allows_only_terraform_files_and_docs(string path) =>
        Assert.Equal(path, InputValidators.GeneratedFilePath(path));

    [Theory]
    [InlineData("run.sh")]
    [InlineData("script.ps1")]
    [InlineData("main.tf.exe")]
    public void GeneratedFilePath_rejects_executables(string path) =>
        Assert.Throws<ArgumentException>(() => InputValidators.GeneratedFilePath(path));

    [Fact]
    public void Image_validates_signature_matches_declared_type()
    {
        var chat = new ChatSettings();
        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00 };
        var jpeg = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00 };

        Assert.Equal("image/png", InputValidators.Image("image/png", png, chat));
        Assert.Equal("image/jpeg", InputValidators.Image("IMAGE/JPEG", jpeg, chat));
        Assert.Throws<ArgumentException>(() => InputValidators.Image("image/png", jpeg, chat));
        Assert.Throws<ArgumentException>(() => InputValidators.Image("image/gif", png, chat));
        Assert.Throws<ArgumentException>(() => InputValidators.Image("image/png", Array.Empty<byte>(), chat));
        Assert.Throws<ArgumentException>(() => InputValidators.Image("image/png", png, new ChatSettings { MaxImageBytes = 4 }));
    }

    [Fact]
    public void Ids_and_messages_are_validated()
    {
        Assert.Equal("demo-1", InputValidators.ProjectId(" Demo-1 "));
        Assert.Throws<ArgumentException>(() => InputValidators.ProjectId("../x"));
        Assert.Equal("SEC-001", InputValidators.PolicyId("sec-001"));
        Assert.Throws<ArgumentException>(() => InputValidators.PolicyId("SEC1"));
        Assert.Throws<ArgumentException>(() => InputValidators.ConversationId("a b"));
        Assert.Throws<ArgumentException>(() => InputValidators.Message("   ", 100));
        Assert.Throws<ArgumentException>(() => InputValidators.Message(new string('x', 101), 100));
    }
}

