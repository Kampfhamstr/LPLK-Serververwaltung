using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using LPLK.Common;
using SwiftlyS2.Shared.Events;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.GameHooks;
using SwiftlyS2.Shared.GameEventDefinitions;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.Plugins;
using SwiftlyS2.Shared.ProtobufDefinitions;

namespace LPLK_ChatGuard;

public sealed partial class Plugin
{
    private static string Normalize(string value)=>ChatGuardPolicy.Normalize(value);
    private static string NormalizeDomain(string value)=>ChatGuardPolicy.NormalizeDomain(value);
    private static string NormalizeGameRole(string? value)=>ChatGuardPolicy.NormalizeGameRole(value);
    private static string FormatGameRole(string? value)=>ChatGuardPolicy.FormatGameRole(value);
    private static string SanitizeForChat(string value)
        => Regex.Replace((value ?? string.Empty).Replace("\r", " ").Replace("\n", " "), @"\s+", " ").Trim()[..Math.Min(40, Regex.Replace((value ?? string.Empty).Replace("\r", " ").Replace("\n", " "), @"\s+", " ").Trim().Length)];

    private static string SanitizeForLog(string value)
    {
        string s = Regex.Replace((value ?? string.Empty).Replace("\r", " ").Replace("\n", " "), @"\s+", " ").Trim();
        return s.Length <= 500 ? s : s[..500];
    }
}

