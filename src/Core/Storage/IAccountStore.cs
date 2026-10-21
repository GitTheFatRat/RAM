namespace RbxManager.Core.Storage;

using System.Collections.Generic;
using System.Threading.Tasks;
using RbxManager.Core.Common;
using RbxManager.Core.Models;

public interface IAccountStore
{
    string VaultPath { get; }
    Task<IReadOnlyList<Account>> ListAsync();
    Task<Result> AddAsync(Account account);
    Task<Result> UpdateAsync(Account account);
    Task RemoveAsync(AccountId id);
}
