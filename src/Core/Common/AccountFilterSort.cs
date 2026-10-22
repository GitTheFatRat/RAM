namespace RbxManager.Core.Common;

using System;
using System.Collections.Generic;
using System.Linq;
using RbxManager.Core.Models;

public static class AccountFilterSort
{
    public static IEnumerable<Account> FilterAndSort(
        IEnumerable<Account> accounts,
        string? selectedGroup,
        string? searchText,
        string sortColumn,
        bool sortDescending)
    {
        IEnumerable<Account> query = accounts;

        if (!string.IsNullOrEmpty(selectedGroup) && !string.Equals(selectedGroup, "All", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(a => string.Equals(a.Group, selectedGroup, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(searchText))
        {
            string search = searchText.Trim();
            query = query.Where(a =>
                a.Alias.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                (a.Group != null && a.Group.Contains(search, StringComparison.OrdinalIgnoreCase)) ||
                (a.Note != null && a.Note.Contains(search, StringComparison.OrdinalIgnoreCase)) ||
                (a.Username != null && a.Username.Contains(search, StringComparison.OrdinalIgnoreCase)));
        }

        Func<Account, object?> keySelector = sortColumn switch
        {
            "Group" => a => a.Group ?? string.Empty,
            "Username" => a => a.Username ?? string.Empty,
            "Status" => a => (int)a.Status,
            "LastChecked" => a => a.LastCheckedAt ?? DateTimeOffset.MinValue,
            "Created" => a => a.CreatedAt,
            _ => a => a.Alias
        };

        return sortDescending
            ? query.OrderByDescending(keySelector)
            : query.OrderBy(keySelector);
    }
}
