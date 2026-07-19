using ScintillaNET;

namespace CodeViewer;

/// <summary>Semantic syntax-highlighting tokens; each maps to a color in the active theme's palette.</summary>
enum Tok { Default, Comment, Keyword, Type, Str, Num, Preproc, Err, Property, Tag, Attr, Regex, Muted }

/// <summary>Maps file extensions to Scintilla lexers, keywords, and dark/light color palettes.</summary>
static class Languages
{
    private sealed record StyleRule(int[] Ids, Tok Token, bool Bold = false, bool Italic = false);
    private sealed record Lang(string Lexer, string Display, string? Kw0 = null, string? Kw1 = null);

    // palettes: raw RGB per semantic token, light (VS-light) and dark (VS Code Dark+)
    private static readonly Dictionary<Tok, int> LightPalette = new()
    {
        [Tok.Default] = 0x000000,
        [Tok.Comment] = 0x008000,
        [Tok.Keyword] = 0x0000FF,
        [Tok.Type] = 0x2B91AF,
        [Tok.Str] = 0xA31515,
        [Tok.Num] = 0x098658,
        [Tok.Preproc] = 0xAF00DB,
        [Tok.Err] = 0xCD0000,
        [Tok.Property] = 0x0451A5,
        [Tok.Tag] = 0x800000,
        [Tok.Attr] = 0xE50000,
        [Tok.Regex] = 0x811F3F,
        [Tok.Muted] = 0x808080,
    };

    private static readonly Dictionary<Tok, int> DarkPalette = new()
    {
        [Tok.Default] = 0xD4D4D4,
        [Tok.Comment] = 0x6A9955,
        [Tok.Keyword] = 0x569CD6,
        [Tok.Type] = 0x4EC9B0,
        [Tok.Str] = 0xCE9178,
        [Tok.Num] = 0xB5CEA8,
        [Tok.Preproc] = 0xC586C0,
        [Tok.Err] = 0xF44747,
        [Tok.Property] = 0x9CDCFE,
        [Tok.Tag] = 0x569CD6,
        [Tok.Attr] = 0x9CDCFE,
        [Tok.Regex] = 0xD16969,
        [Tok.Muted] = 0x6E7681,
    };

    // per-lexer style tables (raw SCE_* style numbers, uniform across ScintillaNET versions)
    private static readonly Dictionary<string, StyleRule[]> LexerStyles = new()
    {
        ["cpp"] = new[]
        {
            new StyleRule(new[] { 1, 2, 3, 15, 17, 18 }, Tok.Comment),
            new StyleRule(new[] { 4 }, Tok.Num),
            new StyleRule(new[] { 5 }, Tok.Keyword, Bold: true),
            new StyleRule(new[] { 6, 7, 12, 13, 20 }, Tok.Str),
            new StyleRule(new[] { 14 }, Tok.Regex),
            new StyleRule(new[] { 9 }, Tok.Preproc),
            new StyleRule(new[] { 16, 19 }, Tok.Type),       // word2, globalclass
        },
        ["python"] = new[]
        {
            new StyleRule(new[] { 1, 12 }, Tok.Comment),
            new StyleRule(new[] { 2 }, Tok.Num),
            new StyleRule(new[] { 3, 4, 6, 7, 13, 16, 17, 18, 19 }, Tok.Str),
            new StyleRule(new[] { 5 }, Tok.Keyword, Bold: true),
            new StyleRule(new[] { 8 }, Tok.Type, Bold: true), // class name
            new StyleRule(new[] { 9 }, Tok.Type),             // def name
            new StyleRule(new[] { 14 }, Tok.Keyword),         // word2
            new StyleRule(new[] { 15 }, Tok.Preproc),         // decorator
        },
        ["json"] = new[]
        {
            new StyleRule(new[] { 1 }, Tok.Num),
            new StyleRule(new[] { 2 }, Tok.Str),
            new StyleRule(new[] { 4 }, Tok.Property),         // property name
            new StyleRule(new[] { 5 }, Tok.Preproc),          // escape sequence
            new StyleRule(new[] { 6, 7 }, Tok.Comment),
            new StyleRule(new[] { 11, 12 }, Tok.Keyword, Bold: true),
            new StyleRule(new[] { 13 }, Tok.Err),
        },
        ["yaml"] = new[]
        {
            new StyleRule(new[] { 1 }, Tok.Comment),
            new StyleRule(new[] { 2 }, Tok.Property),         // key / identifier
            new StyleRule(new[] { 3 }, Tok.Keyword, Bold: true), // true/false/null
            new StyleRule(new[] { 4 }, Tok.Num),
            new StyleRule(new[] { 5, 6 }, Tok.Preproc),       // &anchor, --- document
            new StyleRule(new[] { 8 }, Tok.Err),
        },
        ["xml"] = new[]
        {
            new StyleRule(new[] { 1, 2 }, Tok.Tag),
            new StyleRule(new[] { 3, 4 }, Tok.Attr),
            new StyleRule(new[] { 5 }, Tok.Num),
            new StyleRule(new[] { 6, 7 }, Tok.Keyword),       // attribute values
            new StyleRule(new[] { 9 }, Tok.Comment),
            new StyleRule(new[] { 10 }, Tok.Preproc),         // entity
            new StyleRule(new[] { 17 }, Tok.Muted),           // cdata
        },
        ["hypertext"] = new[]
        {
            new StyleRule(new[] { 1, 2 }, Tok.Tag),
            new StyleRule(new[] { 3, 4 }, Tok.Attr),
            new StyleRule(new[] { 5 }, Tok.Num),
            new StyleRule(new[] { 6, 7 }, Tok.Keyword),
            new StyleRule(new[] { 9 }, Tok.Comment),
            new StyleRule(new[] { 10 }, Tok.Preproc),
            new StyleRule(new[] { 17 }, Tok.Muted),
        },
        ["css"] = new[]
        {
            new StyleRule(new[] { 9 }, Tok.Comment),
            new StyleRule(new[] { 1 }, Tok.Tag),              // tag selector
            new StyleRule(new[] { 2, 10 }, Tok.Type),         // .class, #id
            new StyleRule(new[] { 3, 4, 12 }, Tok.Preproc),   // :pseudo, @directive
            new StyleRule(new[] { 6, 7, 15 }, Tok.Property),  // property names
            new StyleRule(new[] { 8 }, Tok.Str),              // values
            new StyleRule(new[] { 13, 14 }, Tok.Str),
            new StyleRule(new[] { 11 }, Tok.Err),             // !important
        },
        ["sql"] = new[]
        {
            new StyleRule(new[] { 1, 2, 3 }, Tok.Comment),
            new StyleRule(new[] { 4 }, Tok.Num),
            new StyleRule(new[] { 5 }, Tok.Keyword, Bold: true),
            new StyleRule(new[] { 6, 7 }, Tok.Str),
        },
        ["bash"] = new[]
        {
            new StyleRule(new[] { 2 }, Tok.Comment),
            new StyleRule(new[] { 3 }, Tok.Num),
            new StyleRule(new[] { 4 }, Tok.Keyword, Bold: true),
            new StyleRule(new[] { 5, 6, 12, 13 }, Tok.Str),
            new StyleRule(new[] { 9, 10 }, Tok.Preproc),      // $var, ${param}
            new StyleRule(new[] { 11 }, Tok.Regex),           // backticks
            new StyleRule(new[] { 1 }, Tok.Err),
        },
        ["powershell"] = new[]
        {
            new StyleRule(new[] { 1, 13, 16 }, Tok.Comment),
            new StyleRule(new[] { 2, 3, 14, 15 }, Tok.Str),
            new StyleRule(new[] { 4 }, Tok.Num),
            new StyleRule(new[] { 5 }, Tok.Preproc),          // $variable
            new StyleRule(new[] { 8 }, Tok.Keyword, Bold: true),
            new StyleRule(new[] { 9, 10, 11 }, Tok.Type),     // cmdlet, alias, function
        },
        ["markdown"] = new[]
        {
            new StyleRule(new[] { 6, 7, 8, 9, 10, 11 }, Tok.Keyword, Bold: true), // headers
            new StyleRule(new[] { 2, 3 }, Tok.Default, Bold: true),
            new StyleRule(new[] { 4, 5 }, Tok.Default, Italic: true),
            new StyleRule(new[] { 13, 14 }, Tok.Preproc),     // list markers
            new StyleRule(new[] { 15 }, Tok.Comment),         // blockquote
            new StyleRule(new[] { 18 }, Tok.Property),        // link
            new StyleRule(new[] { 19, 20, 21 }, Tok.Str),     // code
        },
        ["batch"] = new[]
        {
            new StyleRule(new[] { 1 }, Tok.Comment),
            new StyleRule(new[] { 2 }, Tok.Keyword, Bold: true),
            new StyleRule(new[] { 3 }, Tok.Preproc),          // :label
            new StyleRule(new[] { 5 }, Tok.Property),         // command
        },
        ["props"] = new[]
        {
            new StyleRule(new[] { 1 }, Tok.Comment),
            new StyleRule(new[] { 2 }, Tok.Tag, Bold: true),  // [section]
            new StyleRule(new[] { 5 }, Tok.Property),         // key
            new StyleRule(new[] { 4 }, Tok.Str),              // value
        },
        ["latex"] = new[]
        {
            new StyleRule(new[] { 1, 9 }, Tok.Keyword),       // \commands
            new StyleRule(new[] { 2, 5 }, Tok.Type),          // {tags}
            new StyleRule(new[] { 3, 6 }, Tok.Regex),         // math
            new StyleRule(new[] { 4, 7 }, Tok.Comment),
            new StyleRule(new[] { 8 }, Tok.Str),              // verbatim
            new StyleRule(new[] { 10 }, Tok.Preproc),
            new StyleRule(new[] { 11 }, Tok.Muted),           // [options]
            new StyleRule(new[] { 12 }, Tok.Err),
        },
        ["makefile"] = new[]
        {
            new StyleRule(new[] { 1 }, Tok.Comment),
            new StyleRule(new[] { 2 }, Tok.Preproc),
            new StyleRule(new[] { 3 }, Tok.Property),
            new StyleRule(new[] { 5 }, Tok.Tag, Bold: true),  // target
            new StyleRule(new[] { 9 }, Tok.Err),
        },
    };

    private const string JsKw0 = "abstract arguments async await break case catch class const continue debugger default delete do else enum export extends finally for from function get if implements import in instanceof interface let new of package private protected public return set static super switch this throw try typeof var void while with yield as declare module namespace readonly keyof infer satisfies type";
    private const string JsKw1 = "true false null undefined NaN Infinity any boolean number string object symbol bigint unknown never void Array Promise Record Partial Map Set Date RegExp Error console window document require";
    private const string CsKw0 = "abstract as base break case catch checked class const continue default delegate do else enum event explicit extern false finally fixed for foreach goto if implicit in interface internal is lock namespace new null operator out override params partial private protected public readonly record ref return sealed sizeof stackalloc static struct switch this throw true try typeof unchecked unsafe using virtual volatile while async await when where yield init required get set value global file scoped";
    private const string CsKw1 = "bool byte char decimal double dynamic float int long nint nuint object sbyte short string uint ulong ushort var void Task List Dictionary IEnumerable String Console Exception";
    private const string JavaKw0 = "abstract assert break case catch class const continue default do else enum extends final finally for goto if implements import instanceof interface native new package private protected public return static strictfp super switch synchronized this throw throws transient try volatile while var record sealed permits yield true false null";
    private const string JavaKw1 = "boolean byte char double float int long short void String Object Integer Long Double Boolean List Map Set ArrayList HashMap System Exception";
    private const string CppKw0 = "alignas alignof and asm auto break case catch class concept const constexpr const_cast continue decltype default delete do dynamic_cast else enum explicit export extern false final for friend goto if inline mutable namespace new noexcept not nullptr operator or override private protected public register reinterpret_cast requires return sizeof static static_assert static_cast struct switch template this throw true try typedef typeid typename union using virtual volatile while";
    private const string CppKw1 = "bool char char16_t char32_t double float int long short signed unsigned void wchar_t size_t string vector map set unique_ptr shared_ptr";
    private const string GoKw0 = "break case chan const continue default defer else fallthrough for func go goto if import interface map package range return select struct switch type var true false nil iota";
    private const string GoKw1 = "bool byte complex64 complex128 error float32 float64 int int8 int16 int32 int64 rune string uint uint8 uint16 uint32 uint64 uintptr make len cap new append copy delete panic recover";
    private const string RustKw0 = "as async await break const continue crate dyn else enum extern false fn for if impl in let loop match mod move mut pub ref return self Self static struct super trait true type unsafe use where while";
    private const string RustKw1 = "bool char f32 f64 i8 i16 i32 i64 i128 isize str u8 u16 u32 u64 u128 usize String Vec Option Result Box Rc Arc";
    private const string TfKw0 = "resource variable output module provider data locals terraform backend required_providers for_each count depends_on lifecycle dynamic true false null var local each";
    private const string SqlKw0 = "add all alter and any as asc backup begin between by case check column constraint create cross database declare default delete desc distinct drop else end exec exists foreign from full group having if in index inner insert into is join key left like limit merge not null offset on or order outer primary procedure replace right rollback select set table then top transaction trigger truncate union unique update values view when where while with commit count sum avg min max";
    private const string BashKw0 = "if then else elif fi for while until do done case esac function in select time break continue return exit export local readonly shift source alias cd echo eval exec printf pwd read set test trap unset";
    private const string PsKw0 = "begin break catch class continue data define do dynamicparam else elseif end exit filter finally for foreach from function if in param process return switch throw trap try until using var while workflow";
    private const string BatKw0 = "rem set if exist errorlevel for in do break call cd chdir choice cls copy del dir echo else exit goto md mkdir move not nul path pause prompt rd ren rename rmdir shift start time title type ver vol defined";
    private const string YamlKw0 = "true false yes no on off null ~";
    private const string JsonKw0 = "true false null";

    private static readonly Dictionary<string, Lang> ByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".js"] = new("cpp", "JavaScript", JsKw0, JsKw1),
        [".mjs"] = new("cpp", "JavaScript", JsKw0, JsKw1),
        [".cjs"] = new("cpp", "JavaScript", JsKw0, JsKw1),
        [".jsx"] = new("cpp", "JavaScript JSX", JsKw0, JsKw1),
        [".ts"] = new("cpp", "TypeScript", JsKw0, JsKw1),
        [".tsx"] = new("cpp", "TypeScript JSX", JsKw0, JsKw1),
        [".cs"] = new("cpp", "C#", CsKw0, CsKw1),
        [".java"] = new("cpp", "Java", JavaKw0, JavaKw1),
        [".c"] = new("cpp", "C", CppKw0, CppKw1),
        [".h"] = new("cpp", "C/C++ Header", CppKw0, CppKw1),
        [".cpp"] = new("cpp", "C++", CppKw0, CppKw1),
        [".hpp"] = new("cpp", "C++", CppKw0, CppKw1),
        [".cc"] = new("cpp", "C++", CppKw0, CppKw1),
        [".go"] = new("cpp", "Go", GoKw0, GoKw1),
        [".rs"] = new("cpp", "Rust", RustKw0, RustKw1),
        [".php"] = new("cpp", "PHP", JsKw0, JsKw1),
        [".kt"] = new("cpp", "Kotlin", JavaKw0, JavaKw1),
        [".proto"] = new("cpp", "Protobuf", "syntax package import option message enum service rpc returns repeated optional required oneof map reserved true false", "double float int32 int64 uint32 uint64 sint32 sint64 fixed32 fixed64 sfixed32 sfixed64 bool string bytes"),
        [".tf"] = new("cpp", "Terraform", TfKw0),
        [".tfvars"] = new("cpp", "Terraform", TfKw0),
        [".py"] = new("python", "Python", "False None True and as assert async await break class continue def del elif else except finally for from global if import in is lambda nonlocal not or pass raise return try while with yield match case", "self cls print len range int str float list dict set tuple type super object Exception"),
        [".json"] = new("json", "JSON", JsonKw0),
        [".jsonc"] = new("json", "JSON", JsonKw0),
        [".tfstate"] = new("json", "JSON", JsonKw0),
        [".yml"] = new("yaml", "YAML", YamlKw0),
        [".yaml"] = new("yaml", "YAML", YamlKw0),
        [".xml"] = new("xml", "XML"),
        [".csproj"] = new("xml", "XML"),
        [".props"] = new("xml", "XML"),
        [".targets"] = new("xml", "XML"),
        [".config"] = new("xml", "XML"),
        [".xaml"] = new("xml", "XML"),
        [".svg"] = new("xml", "XML"),
        [".resx"] = new("xml", "XML"),
        [".plist"] = new("xml", "XML"),
        [".html"] = new("hypertext", "HTML"),
        [".htm"] = new("hypertext", "HTML"),
        [".css"] = new("css", "CSS"),
        [".scss"] = new("css", "SCSS"),
        [".sql"] = new("sql", "SQL", SqlKw0),
        [".sh"] = new("bash", "Shell", BashKw0),
        [".bash"] = new("bash", "Shell", BashKw0),
        [".zsh"] = new("bash", "Shell", BashKw0),
        [".ps1"] = new("powershell", "PowerShell", PsKw0),
        [".psm1"] = new("powershell", "PowerShell", PsKw0),
        [".psd1"] = new("powershell", "PowerShell", PsKw0),
        [".md"] = new("markdown", "Markdown"),
        [".markdown"] = new("markdown", "Markdown"),
        [".bat"] = new("batch", "Batch", BatKw0),
        [".cmd"] = new("batch", "Batch", BatKw0),
        [".ini"] = new("props", "INI"),
        [".env"] = new("props", "Env"),
        [".properties"] = new("props", "Properties"),
        [".toml"] = new("props", "TOML"),
        [".editorconfig"] = new("props", "INI"),
        [".gitignore"] = new("props", "Ignore"),
        [".mk"] = new("makefile", "Makefile"),
        [".tex"] = new("latex", "LaTeX"),
        [".sty"] = new("latex", "LaTeX"),
        [".cls"] = new("latex", "LaTeX"),
        [".bib"] = new("latex", "BibTeX"),
        [".dockerignore"] = new("props", "Ignore"),
    };

    private static readonly Dictionary<string, Lang> ByFileName = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Dockerfile"] = new("bash", "Dockerfile", BashKw0 + " FROM RUN CMD LABEL EXPOSE ENV ADD COPY ENTRYPOINT VOLUME USER WORKDIR ARG HEALTHCHECK SHELL from run cmd label expose env add copy entrypoint volume user workdir arg healthcheck shell"),
        ["Makefile"] = new("makefile", "Makefile"),
        [".gitignore"] = new("props", "Ignore"),
        [".env"] = new("props", "Env"),
    };

    /// <summary>Applies lexer, keywords, and theme colors for the given file. Returns a display name for the status bar.</summary>
    public static string Apply(Scintilla editor, string? filePath, Theme theme)
    {
        Lang? lang = null;
        if (filePath != null)
        {
            var fileName = Path.GetFileName(filePath);
            if (!ByFileName.TryGetValue(fileName, out lang))
                ByExtension.TryGetValue(Path.GetExtension(filePath), out lang);
        }

        if (lang == null)
        {
            editor.LexerName = null;
            return "Plain text";
        }

        editor.LexerName = lang.Lexer;
        if (lang.Kw0 != null) editor.SetKeywords(0, lang.Kw0);
        if (lang.Kw1 != null) editor.SetKeywords(1, lang.Kw1);

        if (LexerStyles.TryGetValue(lang.Lexer, out var rules))
        {
            var palette = theme.IsDark ? DarkPalette : LightPalette;
            foreach (var rule in rules)
            {
                var rgb = palette[rule.Token];
                var color = Color.FromArgb((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
                foreach (var id in rule.Ids)
                {
                    editor.Styles[id].ForeColor = color;
                    editor.Styles[id].Bold = rule.Bold;
                    editor.Styles[id].Italic = rule.Italic;
                }
            }
        }
        return lang.Display;
    }
}
