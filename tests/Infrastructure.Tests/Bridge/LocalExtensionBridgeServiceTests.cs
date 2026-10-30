namespace RbxManager.Infrastructure.Tests.Bridge;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RbxManager.Core.Auth;
using RbxManager.Core.Bridge;
using RbxManager.Core.Common;
using RbxManager.Core.Models;
using RbxManager.Core.Storage;
using RbxManager.Infrastructure.Bridge;

public sealed class LocalExtensionBridgeServiceTests
{
    private const string ValidSampleCookie =
        "_|WARNING:-DO-NOT-SHARE-THIS.--Sharing-this-will-allow-someone-to-log-in-as-you-and-to-steal-your-ROBUX-and-items.|_TEST_TOKEN_123";

    private static int GetFreeLoopbackPort()
    {
        using TcpListener tcpListener = new(IPAddress.Loopback, 0);
        tcpListener.Start();
        return ((IPEndPoint)tcpListener.LocalEndpoint).Port;
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("https://www.roblox.com", false)]
    [InlineData("http://127.0.0.1:48123", false)]
    [InlineData("null", false)]
    [InlineData("chrome-extension://abcdefghijklmnop", false)]
    [InlineData("chrome-extension://" + LocalExtensionBridgeService.TrustedExtensionId + ".evil", false)]
    [InlineData("moz-extension://1234-5678", false)]
    [InlineData("extension://edge-ext-id", false)]
    [InlineData(LocalExtensionBridgeService.TrustedExtensionOrigin, true)]
    [InlineData(LocalExtensionBridgeService.TrustedExtensionOrigin + "/", true)]
    public void IsAllowedExtensionOrigin_ValidatesStrictly(string? origin, bool expected)
    {
        Assert.Equal(expected, LocalExtensionBridgeService.IsAllowedExtensionOrigin(origin));
    }

    [Fact]
    public async Task ImportEndpoint_WhenOriginMissingOrInvalid_Returns403Forbidden()
    {
        int port = GetFreeLoopbackPort();
        FakeAccountStore store = new();
        FakeAuthService auth = new();
        using LocalExtensionBridgeService bridge = new(store, auth, port: port);
        Assert.True(bridge.StartListening());

        using HttpClient client = new();
        using HttpRequestMessage reqWithoutOrigin = new(HttpMethod.Post, bridge.EndpointUrl)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { cookie = ValidSampleCookie }),
                Encoding.UTF8,
                "application/json")
        };

        using HttpResponseMessage resp1 = await client.SendAsync(reqWithoutOrigin);
        Assert.Equal(HttpStatusCode.Forbidden, resp1.StatusCode);

        using HttpRequestMessage reqEvilOrigin = new(HttpMethod.Post, bridge.EndpointUrl)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { cookie = ValidSampleCookie }),
                Encoding.UTF8,
                "application/json")
        };
        reqEvilOrigin.Headers.TryAddWithoutValidation("Origin", "https://evil.example.com");

        using HttpResponseMessage resp2 = await client.SendAsync(reqEvilOrigin);
        Assert.Equal(HttpStatusCode.Forbidden, resp2.StatusCode);
        Assert.Empty(store.Accounts);
    }

    // M44 Flow 5 real loopback: another installed extension (valid chrome-extension:// scheme, wrong ID) must be rejected.
    [Fact]
    public async Task ImportEndpoint_WhenForeignExtensionOrigin_Returns403AndStoresNothing()
    {
        int port = GetFreeLoopbackPort();
        FakeAccountStore store = new();
        using LocalExtensionBridgeService bridge = new(store, new FakeAuthService(), port: port);
        Assert.True(bridge.StartListening());

        using HttpClient client = new();
        foreach (string foreignOrigin in new[] { "chrome-extension://aaaabbbbccccddddeeeeffffgggghhhh", "moz-extension://1234-5678", "null" })
        {
            using HttpRequestMessage request = new(HttpMethod.Post, bridge.EndpointUrl)
            {
                Content = new StringContent(JsonSerializer.Serialize(new { cookie = ValidSampleCookie }), Encoding.UTF8, "application/json")
            };
            request.Headers.TryAddWithoutValidation("Origin", foreignOrigin);

            using HttpResponseMessage response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        Assert.Empty(store.Accounts);
    }

    [Fact]
    public void ExtensionManifest_PinnedKey_DerivesTheTrustedExtensionId()
    {
        string manifestPath = FindRepoFile(Path.Combine("tools", "BrowserExtension", "manifest.json"));
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
        byte[] publicKey = Convert.FromBase64String(manifest.RootElement.GetProperty("key").GetString()!);

        string hex = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(publicKey), 0, 16).ToLowerInvariant();
        string derivedId = string.Concat(hex.Select(c => (char)('a' + Convert.ToInt32(c.ToString(), 16))));

        Assert.Equal(LocalExtensionBridgeService.TrustedExtensionId, derivedId);
    }

    private static string FindRepoFile(string relativePath)
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException(relativePath);
    }
    [Fact]
    public async Task ImportEndpoint_WhenValidExtensionOriginAndValidCookie_SavesAccountAndReturns200()
    {
        int port = GetFreeLoopbackPort();
        FakeAccountStore store = new();
        FakeAuthService auth = new()
        {
            NextResult = Result.Success(new RobloxUser(987654, "BridgeTester", "BridgeTester"))
        };

        using LocalExtensionBridgeService bridge = new(store, auth, port: port);
        ExtensionImportedAccount? importedEventPayload = null;
        bridge.AccountImported += acc => importedEventPayload = acc;

        Assert.True(bridge.StartListening());

        using HttpClient client = new();
        using HttpRequestMessage request = new(HttpMethod.Post, bridge.EndpointUrl)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { cookie = ValidSampleCookie }),
                Encoding.UTF8,
                "application/json")
        };
        request.Headers.TryAddWithoutValidation("Origin", LocalExtensionBridgeService.TrustedExtensionOrigin);

        using HttpResponseMessage response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string json = await response.Content.ReadAsStringAsync();
        using JsonDocument doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("BridgeTester", doc.RootElement.GetProperty("username").GetString());
        Assert.Equal(987654L, doc.RootElement.GetProperty("userId").GetInt64());

        Assert.Single(store.Accounts);
        Assert.Equal("BridgeTester", store.Accounts[0].Username);
        Assert.Equal(987654L, store.Accounts[0].UserId);
        Assert.NotNull(importedEventPayload);
        Assert.Equal("BridgeTester", importedEventPayload!.Username);
        Assert.False(importedEventPayload.WasUpdated);
    }

    [Fact]
    public async Task ImportEndpoint_WhenValidationFails_Returns422AndDoesNotSave()
    {
        int port = GetFreeLoopbackPort();
        FakeAccountStore store = new();
        FakeAuthService auth = new()
        {
            NextResult = Result.Failure<RobloxUser>("Expired cookie", AuthErrorCodes.Expired)
        };

        using LocalExtensionBridgeService bridge = new(store, auth, port: port);
        Assert.True(bridge.StartListening());

        using HttpClient client = new();
        using HttpRequestMessage request = new(HttpMethod.Post, bridge.EndpointUrl)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { cookie = ValidSampleCookie }),
                Encoding.UTF8,
                "application/json")
        };
        request.Headers.TryAddWithoutValidation("Origin", LocalExtensionBridgeService.TrustedExtensionOrigin);

        using HttpResponseMessage response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Empty(store.Accounts);
    }

    [Theory]
    [InlineData("www.roblox.com:443", true)]
    [InlineData("roblox.com:443", true)]
    [InlineData("auth.roblox.com:443", true)]
    [InlineData("www.roblox.com", true)]
    [InlineData("www.roblox.com:80", false)]
    [InlineData("evil-roblox.com:443", false)]
    [InlineData("example.com:443", false)]
    [InlineData("", false)]
    public void LocalSniDpiBypassProxy_TryParseAllowedRobloxTarget_ValidatesHostAndPort(string authority, bool expected)
    {
        bool actual = LocalSniDpiBypassProxy.TryParseAllowedRobloxTarget(authority, out _, out int port);
        Assert.Equal(expected, actual);
        if (actual)
        {
            Assert.Equal(443, port);
        }
    }

    [Fact]
    public async Task LocalSniDpiBypassProxy_TryWriteSplitTlsClientHelloAsync_SplitsSniAcrossTwoValidTlsRecords()
    {
        byte[] payload = Encoding.ASCII.GetBytes("HELLO_PREFIX_www.roblox.com_HELLO_SUFFIX");
        byte[] tlsRecord = new byte[5 + payload.Length];
        tlsRecord[0] = 0x16;
        tlsRecord[1] = 0x03;
        tlsRecord[2] = 0x01;
        tlsRecord[3] = (byte)((payload.Length >> 8) & 0xFF);
        tlsRecord[4] = (byte)(payload.Length & 0xFF);
        Buffer.BlockCopy(payload, 0, tlsRecord, 5, payload.Length);

        using MemoryStream output = new();
        bool split = await LocalSniDpiBypassProxy.TryWriteSplitTlsClientHelloAsync(
            output,
            tlsRecord,
            tlsRecord.Length,
            CancellationToken.None);

        Assert.True(split);

        byte[] written = output.ToArray();
        Assert.Equal(tlsRecord.Length + 5, written.Length);

        int len1 = (written[3] << 8) | written[4];
        string part1 = Encoding.ASCII.GetString(written, 5, len1);
        Assert.DoesNotContain("roblox.com", part1, StringComparison.OrdinalIgnoreCase);

        int rec2Offset = 5 + len1;
        Assert.Equal(0x16, written[rec2Offset]);
        int len2 = (written[rec2Offset + 3] << 8) | written[rec2Offset + 4];
        string part2 = Encoding.ASCII.GetString(written, rec2Offset + 5, len2);
        Assert.DoesNotContain("roblox.com", part2, StringComparison.OrdinalIgnoreCase);

        Assert.Equal("HELLO_PREFIX_www.roblox.com_HELLO_SUFFIX", part1 + part2);
    }

    private sealed class FakeAuthService : IRobloxAuthService
    {
        public Result<RobloxUser> NextResult { get; set; } =
            Result.Success(new RobloxUser(1, "DefaultUser", "DefaultUser"));

        public Task<Result<RobloxUser>> ValidateCookieAsync(SecretValue cookie, CancellationToken ct = default)
            => Task.FromResult(NextResult);

        public Task<Result<SecretValue>> GetAuthTicketAsync(SecretValue cookie, CancellationToken ct = default)
            => Task.FromResult(Result.Success(SecretValue.FromString("TICKET")));
    }

    private sealed class FakeAccountStore : IAccountStore
    {
        public List<Account> Accounts { get; } = new();

        public string VaultPath => "memory://vault.dat";

        public Task<IReadOnlyList<Account>> ListAsync(CancellationToken ct = default)
        {
            IReadOnlyList<Account> snapshot = Accounts
                .Select(a => a with { Cookie = a.Cookie.Clone() })
                .ToList();
            return Task.FromResult(snapshot);
        }

        public Task<Result> AddAsync(Account account, CancellationToken ct = default)
        {
            Accounts.Add(account with { Cookie = account.Cookie.Clone() });
            return Task.FromResult(Result.Success());
        }

        public Task<Result> UpdateAsync(Account account, CancellationToken ct = default)
        {
            int idx = Accounts.FindIndex(a => a.Id == account.Id);
            if (idx < 0)
            {
                return Task.FromResult(Result.Failure("Not found", "NOT_FOUND"));
            }

            Accounts[idx].Cookie.Dispose();
            Accounts[idx] = account with { Cookie = account.Cookie.Clone() };
            return Task.FromResult(Result.Success());
        }

        public Task RemoveAsync(AccountId id, CancellationToken ct = default)
        {
            Accounts.RemoveAll(a => a.Id == id);
            return Task.CompletedTask;
        }
    }
}
