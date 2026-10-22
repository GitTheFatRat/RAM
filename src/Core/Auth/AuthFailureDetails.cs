namespace RbxManager.Core.Auth;

using System.Collections.Generic;

public sealed record AuthFailureDetails(
    string Stage,
    int? HttpStatus,
    IReadOnlyList<int> RobloxErrorCodes,
    string? RobloxMessage,
    bool CsrfTokenObtained,
    IReadOnlyList<string> ResponseHeaderNames,
    bool TicketHeaderPresent);
