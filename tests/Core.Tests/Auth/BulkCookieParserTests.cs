namespace RbxManager.Core.Tests.Auth;

using System.Collections.Generic;
using RbxManager.Core.Auth;
using RbxManager.Core.Models;
using Xunit;

public sealed class BulkCookieParserTests
{
    private const string SampleWarningCookie1 = "_|WARNING:-DO-NOT-SHARE-THIS.--Sharing-this-will-allow-someone-to-log-in._|COOKIE_ONE_111";
    private const string SampleWarningCookie2 = "_|WARNING:-DO-NOT-SHARE-THIS.--Sharing-this-will-allow-someone-to-log-in._|COOKIE_TWO_222";

    [Fact]
    public void ParseCookies_EmptyOrWhitespace_ReturnsEmpty()
    {
        Assert.Empty(BulkCookieParser.ParseCookies(null));
        Assert.Empty(BulkCookieParser.ParseCookies(""));
        Assert.Empty(BulkCookieParser.ParseCookies("   \r\n  \n "));
    }

    [Fact]
    public void ParseCookies_MultipleFormats_ExtractsAndDeduplicates()
    {
        string input = $"""
            {SampleWarningCookie1}
            PlayerTwo:MySecretPass123:{SampleWarningCookie2}
            PlayerOneDup:OtherPass:{SampleWarningCookie1}
            .ROBLOSECURITY={SampleWarningCookie2}; Domain=.roblox.com
            UserThree:PassThree:SYNTHETIC_COOKIE_TOKEN_333
            """;

        IReadOnlyList<SecretValue> cookies = BulkCookieParser.ParseCookies(input);
        try
        {
            Assert.Equal(3, cookies.Count);
            Assert.All(cookies, c => Assert.True(c.HasContent));
        }
        finally
        {
            foreach (SecretValue cookie in cookies)
            {
                cookie.Dispose();
            }
        }
    }
}
