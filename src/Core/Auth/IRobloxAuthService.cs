namespace RbxManager.Core.Auth;

using System.Threading;
using System.Threading.Tasks;
using RbxManager.Core.Common;
using RbxManager.Core.Models;

public interface IRobloxAuthService
{
    Task<Result<RobloxUser>> ValidateCookieAsync(SecretValue cookie, ProxyConfig? proxy = null, CancellationToken ct = default);
    Task<Result<SecretValue>> GetAuthTicketAsync(SecretValue cookie, ProxyConfig? proxy = null, CancellationToken ct = default);
}
