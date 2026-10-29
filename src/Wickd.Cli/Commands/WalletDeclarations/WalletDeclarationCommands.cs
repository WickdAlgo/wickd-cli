using System.ComponentModel;
using Spectre.Console.Cli;
using Wickd.Cli.Commands.Journal;
using Wickd.Cli.Commands.Settings;
using Wickd.Cli.Commands.WalletCapital;
using Wickd.Cli.Models;
using Wickd.Cli.Services;

namespace Wickd.Cli.Commands.WalletDeclarations;

/// <summary>Original raw assertion file, explicit retry identity and the wallet's current declaration stream ETag.</summary>
public sealed class WalletDeclarationCreateSettings : WriteSettings
{
    /// <summary>The authenticated workspace's native paper wallet.</summary>
    [CommandArgument(0, "<WALLET_ID>")]
    public string WalletId { get; init; } = string.Empty;
    /// <summary>Strong stream ETag returned by the wallet's declaration list.</summary>
    [CommandOption("--if-match <ETAG>")]
    [Description("Strong declaration stream ETag from `wallet declarations list`.")]
    public string IfMatch { get; init; } = string.Empty;
}

/// <summary>A full replacement of one current declaration with its correction explanation.</summary>
public sealed class WalletDeclarationCorrectSettings : WriteSettings
{
    /// <summary>The authenticated workspace's native paper wallet.</summary>
    [CommandArgument(0, "<WALLET_ID>")]
    public string WalletId { get; init; } = string.Empty;
    /// <summary>The declaration being replaced; it must be the current tip of its chain.</summary>
    [CommandArgument(1, "<DECLARATION_ID>")]
    public string DeclarationId { get; init; } = string.Empty;
    /// <summary>Strong stream ETag returned by the wallet's declaration list.</summary>
    [CommandOption("--if-match <ETAG>")]
    [Description("Strong declaration stream ETag from `wallet declarations list`.")]
    public string IfMatch { get; init; } = string.Empty;
}

/// <summary>One server-owned frozen page of a wallet's declarations.</summary>
public sealed class WalletDeclarationListSettings : GlobalCommandSettings
{
    /// <summary>The authenticated workspace's native paper wallet.</summary>
    [CommandArgument(0, "<WALLET_ID>")]
    public string WalletId { get; init; } = string.Empty;
    /// <summary>Maximum number of declarations in one page.</summary>
    [CommandOption("--limit <COUNT>")]
    [DefaultValue(10)]
    public int Limit { get; init; } = 10;
    /// <summary>Opaque next cursor from this wallet's preceding page.</summary>
    [CommandOption("--cursor <CURSOR>")]
    public string? Cursor { get; init; }
}

/// <summary>One retained declaration, optionally at the list's frozen snapshot.</summary>
public sealed class WalletDeclarationGetSettings : GlobalCommandSettings
{
    /// <summary>The authenticated workspace's native paper wallet.</summary>
    [CommandArgument(0, "<WALLET_ID>")]
    public string WalletId { get; init; } = string.Empty;
    /// <summary>The retained declaration identity.</summary>
    [CommandArgument(1, "<DECLARATION_ID>")]
    public string DeclarationId { get; init; } = string.Empty;
    /// <summary>Opaque snapshot cursor from this wallet's list.</summary>
    [CommandOption("--cursor <CURSOR>")]
    public string? Cursor { get; init; }
}

/// <summary>Records raw zero-only paper wallet assertions; the server returns no valuation.</summary>
public sealed class WalletDeclarationCreateCommand(IApiClientFactory factory) : WalletCapitalCommand<WalletDeclarationCreateSettings>(factory)
{
    /// <inheritdoc />
    protected override V2Call BuildCall(WalletDeclarationCreateSettings s, byte[]? request) =>
        new(V2Operation.WalletDeclarationCreate, Id: s.WalletId, Request: request, IdempotencyKey: s.IdempotencyKey, IfMatch: s.IfMatch);
}
/// <summary>Appends a corrected replacement while preserving the original declaration.</summary>
public sealed class WalletDeclarationCorrectCommand(IApiClientFactory factory) : WalletCapitalCommand<WalletDeclarationCorrectSettings>(factory)
{
    /// <inheritdoc />
    protected override V2Call BuildCall(WalletDeclarationCorrectSettings s, byte[]? request) =>
        new(V2Operation.WalletDeclarationCorrect, Id: s.WalletId, SubId: s.DeclarationId, Request: request,
            IdempotencyKey: s.IdempotencyKey, IfMatch: s.IfMatch);
}
/// <summary>Reads one frozen page of declarations with separately evaluated source reconciliation.</summary>
public sealed class WalletDeclarationListCommand(IApiClientFactory factory) : WalletCapitalCommand<WalletDeclarationListSettings>(factory)
{
    /// <inheritdoc />
    protected override V2Call BuildCall(WalletDeclarationListSettings s, byte[]? request) =>
        new(V2Operation.WalletDeclarationList, Id: s.WalletId, Limit: s.Limit, Cursor: s.Cursor);
}
/// <summary>Reads one declaration at an optional frozen coordinate.</summary>
public sealed class WalletDeclarationGetCommand(IApiClientFactory factory) : WalletCapitalCommand<WalletDeclarationGetSettings>(factory)
{
    /// <inheritdoc />
    protected override V2Call BuildCall(WalletDeclarationGetSettings s, byte[]? request) =>
        new(V2Operation.WalletDeclarationGet, Id: s.WalletId, SubId: s.DeclarationId, Cursor: s.Cursor);
}
