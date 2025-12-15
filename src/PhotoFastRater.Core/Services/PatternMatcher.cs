using System.Text.RegularExpressions;
using PhotoFastRater.Core.Models;

namespace PhotoFastRater.Core.Services;

/// <summary>
/// パターンマッチングサービス
/// </summary>
public static class PatternMatcher
{
    /// <summary>
    /// ファイルパスが除外パターンに一致するかチェック
    /// </summary>
    /// <param name="filePath">チェックするファイルパス</param>
    /// <param name="pattern">除外パターン</param>
    /// <returns>一致する場合true</returns>
    public static bool IsMatch(string filePath, FolderExclusionPattern pattern)
    {
        if (!pattern.IsEnabled)
        {
            return false;
        }

        return pattern.Type switch
        {
            PatternType.Wildcard => MatchWildcard(filePath, pattern.Pattern),
            PatternType.Regex => MatchRegex(filePath, pattern.Pattern),
            PatternType.Exact => MatchExact(filePath, pattern.Pattern),
            _ => false
        };
    }

    /// <summary>
    /// 複数のパターンに対してチェック
    /// </summary>
    /// <param name="filePath">チェックするファイルパス</param>
    /// <param name="patterns">除外パターンのリスト</param>
    /// <returns>いずれかのパターンに一致する場合true</returns>
    public static bool IsMatchAny(string filePath, IEnumerable<FolderExclusionPattern> patterns)
    {
        return patterns.Any(p => IsMatch(filePath, p));
    }

    /// <summary>
    /// ワイルドカードパターンマッチング
    /// 例: */temp/*, */backup/*
    /// </summary>
    private static bool MatchWildcard(string filePath, string pattern)
    {
        try
        {
            // ワイルドカードを正規表現に変換
            var regexPattern = WildcardToRegex(pattern);
            return Regex.IsMatch(filePath, regexPattern, RegexOptions.IgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 正規表現パターンマッチング
    /// 例: ^.*\\temp\\.*$
    /// </summary>
    private static bool MatchRegex(string filePath, string pattern)
    {
        try
        {
            return Regex.IsMatch(filePath, pattern, RegexOptions.IgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 完全一致
    /// 例: D:\Photos\backup
    /// </summary>
    private static bool MatchExact(string filePath, string pattern)
    {
        // パスの正規化（大文字小文字、区切り文字の統一）
        var normalizedFilePath = NormalizePath(filePath);
        var normalizedPattern = NormalizePath(pattern);

        return normalizedFilePath.StartsWith(normalizedPattern, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// ワイルドカードを正規表現に変換
    /// </summary>
    private static string WildcardToRegex(string pattern)
    {
        // パス区切り文字を統一
        pattern = pattern.Replace("/", "\\");

        // 特殊文字をエスケープ
        var escaped = Regex.Escape(pattern);

        // ワイルドカードを正規表現に変換
        // \* → .* (任意の文字列)
        // \? → . (任意の1文字)
        escaped = escaped.Replace("\\*", ".*");
        escaped = escaped.Replace("\\?", ".");

        // 前後にアンカーを追加
        return "^" + escaped + "$";
    }

    /// <summary>
    /// パスを正規化（大文字小文字、区切り文字の統一）
    /// </summary>
    private static string NormalizePath(string path)
    {
        // 区切り文字を統一
        path = path.Replace("/", "\\");

        // 末尾の区切り文字を削除
        path = path.TrimEnd('\\');

        // 小文字に統一
        return path.ToLowerInvariant();
    }
}
