namespace RbxManager.Core.Storage;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RbxManager.Core.Common;
using RbxManager.Core.Models;

public interface IAccountStore
{
    string VaultPath { get; }
    Task<IReadOnlyList<Account>> ListAsync(CancellationToken ct = default);
    Task<Result> AddAsync(Account account, CancellationToken ct = default);
    Task<Result> UpdateAsync(Account account, CancellationToken ct = default);
    Task RemoveAsync(AccountId id, CancellationToken ct = default);
}
