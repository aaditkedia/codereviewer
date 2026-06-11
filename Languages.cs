using ScintillaNET;

namespace CodeViewer;

/// <summary>Maps file extensions to Scintilla lexers, keywords, and a VS-light color scheme.</summary>
static class Languages
{
    private sealed record StyleRule(int[] Ids, int Rgb, bool Bold = false, bool Italic = false);
    private sealed record Lang(string Lexer, string Display, string? Kw0 = null, string? Kw1 = null);

    // palette
    private const int Comment = 0x008000;
    private const int Keyword = 0x0000FF;
    private const int TypeCol = 0x2B91AF;
    private const int Str = 0xA31515;
    private const int Num = 0x098658;
    private const int Preproc = 0xAF00DB;
    private const int ErrCol = 0xCD0000;

    // per-lexer style tables (raw SCE_* style numbers, uniform across ScintillaNET versions)
    private static readonly Dictionary<string, StyleRule[]> LexerStyles = new()
    {
        ["cpp"] = new[]
        {
            new StyleRule(new[] { 1, 2, 3, 15, 17, 18 }, Comment),
            new StyleRule(new[] { 4 }, Num),
            new StyleRule(new[] { 5 }, Keyword, Bold: true),
            new StyleRule(new[] { 6, 7, 12, 13, 20 }, Str),
            new StyleRule(new[] { 14 }, 0x811F3F),          // regex
            new StyleRule(new[] { 9 }, Preproc),
            new StyleRule(new[] { 16, 19 }, TypeCol),       // word2, globalclass
        },
        ["python"] = new[]
        {
            new StyleRule(new[] { 1, 12 }, Comment),
            new StyleRule(new[] { 2 }, Num),
            new StyleRule(new[] { 3, 4, 6, 7, 13, 16, 17, 18, 19 }, Str),
            new StyleRule(new[] { 5 }, Keyword, Bold: true),
            new StyleRule(new[] { 8 }, TypeCol, Bold: true), // class name
            new StyleRule(new[] { 9 }, TypeCol),             // def name
            new StyleRule(new[] { 14 }, Keyword),            // word2
            new StyleRule(new[] { 15 }, Preproc),            // decorator
        },
        ["json"] = new[]
        {
            new StyleRule(new[] { 1 }, Num),
            new StyleRule(new[] { 2 }, Str),
            new StyleRule(new[] { 4 }, 0x0451A5),            // property name
            new StyleRule(new[] { 5 }, Preproc),             // escape sequence
            new StyleRule(new[] { 6, 7 }, Comment),
            new StyleRule(new[] { 11, 12 }, Keyword, Bold: true),
            new StyleRule(new[] { 13 }, ErrCol),
        },
        ["yaml"] = new[]
        {
            new StyleRule(new[] { 1 }, Comment),
            new StyleRule(new[] { 2 }, 0x0451A5),            // key / identifier
            new StyleRule(new[] { 3 }, Keyword, Bold: true), // true/false/null
            new StyleRule(new[] { 4 }, Num),
            new StyleRule(new[] { 5, 6 }, Preproc),          // &anchor, --- document
            new StyleRule(new[] { 8 }, ErrCol),
        },
        ["xml"] = new[]
        {
            new StyleRule(new[] { 1, 2 }, 0x800000),         // tags
            new StyleRule(new[] { 3, 4 }, 0xE50000),         // attributes
            new StyleRule(new[] { 5 }, Num),
            new StyleRule(new[] { 6, 7 }, Keyword),          // attribute values
            new StyleRule(new[] { 9 }, Comment),
            new StyleRule(new[] { 10 }, Preproc),            // entity
            new StyleRule(new[] { 17 }, 0x808080),           // cdata
        },
        ["hypertext"] = new[]
        {
            new StyleRule(new[] { 1, 2 }, 0x800000),
            new StyleRule(new[] { 3, 4 }, 0xE50000),
            new StyleRule(new[] { 5 }, Num),
            new StyleRule(new[] { 6, 7 }, Keyword),
            new StyleRule(new[] { 9 }, Comment),
            new StyleRule(new[] { 10 }, Preproc),
            new StyleRule(new[] { 17 }, 0x808080),
        },
        ["css"] = new[]
        {
            new StyleRule(new[] { 9 }, Comment),
            new StyleRule(new[] { 1 }, 0x800000),            // tag selector
            new StyleRule(new[] { 2, 10 }, TypeCol),         // .class, #id
            new StyleRule(new[] { 3, 4, 12 }, Preproc),      // :pseudo, @directive
            new StyleRule(new[] { 6, 7, 15 }, 0x0451A5),     // property names
            new StyleRule(new[] { 8 }, Str),                 // values
            new StyleRule(new[] { 13, 14 }, Str),
            new StyleRule(new[] { 11 }, ErrCol),             // !important
        },
        ["sql"] = new[]
        {
            new StyleRule(new[] { 1, 2, 3 }, Comment),
            new StyleRule(new[] { 4 }, Num),
            new StyleRule(new[] { 5 }, Keyword, Bold: true),
            new StyleRule(new[] { 6, 7 }, Str),
        },
        ["bash"] = new[]
        {
            new StyleRule(new[] { 2 }, Comment),
            new StyleRule(new[] { 3 }, Num),
            new StyleRule(new[] { 4 }, Keyword, Bold: true),
            new StyleRule(new[] { 5, 6, 12, 13 }, Str),
            new StyleRule(new[] { 9, 10 }, Preproc),         // $var, ${param}
            new StyleRule(new[] { 11 }, 0x811F3F),           // backticks
            new StyleRule(new[] { 1 }, ErrCol),
        },
        ["powershell"] = new[]
        {
            new StyleRule(new[] { 1, 13, 16 }, Comment),
            new StyleRule(new[] { 2, 3, 14, 15 }, Str),
            new StyleRule(new[] { 4 }, Num),
            new StyleRule(new[] { 5 }, Preproc),             // $variable
            new StyleRule(new[] { 8 }, Keyword, Bold: true),
            new StyleRule(new[] { 9, 10, 11 }, TypeCol),     // cmdlet, alias, function
        },
        ["markdown"] = new[]
        {
            new StyleRule(new[] { 6, 7, 8, 9, 10, 11 }, Keyword, Bold: true), // headers
            new StyleRule(new[] { 2, 3 }, 0x000000, Bold: true),
            new StyleRule(new[] { 4, 5 }, 0x000000, Italic: true),
            new StyleRule(new[] { 13, 14 }, Preproc),        // list markers
            new StyleRule(new[] { 15 }, Comment),            // blockquote
            new StyleRule(new[] { 18 }, 0x0451A5),           // link
            new StyleRule(new[] { 19, 20, 21 }, Str),        // code
        },
        ["batch"] = new[]
        {
            new StyleRule(new[] { 1 }, Comment),
            new StyleRule(new[] { 2 }, Keyword, Bold: true),
            new StyleRule(new[] { 3 }, Preproc),             // :label
            new StyleRule(new[] { 5 }, 0x0451A5),            // command
        },
        ["props"] = new[]
        {
            new StyleRule(new[] { 1 }, Comment),
            new StyleRule(new[] { 2 }, 0x800000, Bold: true), // [section]
            new StyleRule(new[] { 5 }, 0x0451A5),             // key
            new StyleRule(new[] { 4 }, Str),                  // value
        },
        ["latex"] = new[]
        {
            new StyleRule(new[] { 1, 9 }, Keyword),           // \commands
            new StyleRule(new[] { 2, 5 }, TypeCol),           // {tags}
            new StyleRule(new[] { 3, 6 }, 0x811F3F),          // math
            new StyleRule(new[] { 4, 7 }, Comment),
            new StyleRule(new[] { 8 }, Str),                  // verbatim
            new StyleRule(new[] { 10 }, Preproc),
            new StyleRule(new[] { 11 }, 0x808080),            // [options]
            new StyleRule(new[] { 12 }, ErrCol),
        },
        ["makefile"] = new[]
        {
            new StyleRule(new[] { 1 }, Comment),
            new StyleRule(new[] { 2 }, Preproc),
            new StyleRule(new[] { 3 }, 0x0451A5),
            new StyleRule(new[] { 5 }, 0x800000, Bold: true), // target
            new StyleRule(new[] { 9 }, ErrCol),
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

    /// <summary>Applies lexer, keywords, and colors for the given file. Returns a display name for the status bar.</summary>
    public static string Apply(Scintilla editor, string? filePath)
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
            foreach (var rule in rules)
            {
                var color = Color.FromArgb((rule.Rgb >> 16) & 0xFF, (rule.Rgb >> 8) & 0xFF, rule.Rgb & 0xFF);
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
