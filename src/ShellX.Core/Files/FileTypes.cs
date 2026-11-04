using System.IO;

namespace ShellX.Core.Files;

/// <summary>按扩展名给文件归类，本地和远程列表共用；类别决定列表图标和“类型”列文字。</summary>
public static class FileTypes
{
    public const string Folder = "folder";
    public const string Link = "link";
    public const string File = "file";
    public const string Code = "code";
    public const string Config = "config";
    public const string Image = "image";
    public const string Audio = "audio";
    public const string Video = "video";
    public const string Archive = "archive";
    public const string Text = "text";
    public const string Sheet = "sheet";
    public const string Program = "program";
    public const string Database = "db";
    public const string Pdf = "pdf";
    public const string Document = "doc";

    // 图标控件按这些类别提供图形，新增类别必须同步补图标。
    public static IReadOnlyList<string> All { get; } =
        [Folder, Link, File, Code, Config, Image, Audio, Video, Archive, Text, Sheet, Program, Database, Pdf, Document];

    private static readonly Dictionary<string, string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        // 代码与脚本
        ["cs"] = Code, ["c"] = Code, ["cpp"] = Code, ["cxx"] = Code, ["h"] = Code, ["hpp"] = Code, ["hxx"] = Code,
        ["js"] = Code, ["mjs"] = Code, ["cjs"] = Code, ["ts"] = Code, ["jsx"] = Code, ["tsx"] = Code,
        ["py"] = Code, ["pyw"] = Code, ["go"] = Code, ["java"] = Code, ["kt"] = Code, ["kts"] = Code,
        ["swift"] = Code, ["rs"] = Code, ["php"] = Code, ["rb"] = Code, ["lua"] = Code, ["vue"] = Code,
        ["html"] = Code, ["htm"] = Code, ["css"] = Code, ["scss"] = Code, ["sass"] = Code, ["less"] = Code,
        ["sh"] = Code, ["bash"] = Code, ["zsh"] = Code, ["fish"] = Code, ["bat"] = Code, ["cmd"] = Code,
        ["ps1"] = Code, ["psm1"] = Code, ["psd1"] = Code, ["sql"] = Code, ["dart"] = Code, ["r"] = Code,
        ["pl"] = Code, ["pm"] = Code, ["groovy"] = Code, ["gradle"] = Code, ["m"] = Code, ["mm"] = Code,
        ["asm"] = Code, ["v"] = Code, ["sv"] = Code, ["vb"] = Code, ["fs"] = Code, ["fsx"] = Code,
        ["ex"] = Code, ["exs"] = Code, ["erl"] = Code, ["clj"] = Code, ["scala"] = Code, ["proto"] = Code,
        ["ipynb"] = Code, ["svelte"] = Code, ["astro"] = Code,
        // 配置、密钥与描述文件
        ["json"] = Config, ["jsonc"] = Config, ["yaml"] = Config, ["yml"] = Config, ["toml"] = Config,
        ["ini"] = Config, ["conf"] = Config, ["cfg"] = Config, ["config"] = Config, ["env"] = Config,
        ["properties"] = Config, ["xml"] = Config, ["plist"] = Config, ["editorconfig"] = Config,
        ["gitignore"] = Config, ["gitattributes"] = Config, ["dockerignore"] = Config, ["npmrc"] = Config,
        ["service"] = Config, ["mount"] = Config, ["target"] = Config, ["pem"] = Config, ["key"] = Config,
        ["crt"] = Config, ["cer"] = Config, ["csr"] = Config, ["p12"] = Config, ["pfx"] = Config, ["pub"] = Config,
        // 图片
        ["png"] = Image, ["jpg"] = Image, ["jpeg"] = Image, ["gif"] = Image, ["bmp"] = Image, ["webp"] = Image,
        ["svg"] = Image, ["ico"] = Image, ["tif"] = Image, ["tiff"] = Image, ["heic"] = Image, ["heif"] = Image,
        ["avif"] = Image, ["raw"] = Image, ["psd"] = Image, ["jfif"] = Image, ["apng"] = Image,
        // 音频
        ["mp3"] = Audio, ["wav"] = Audio, ["flac"] = Audio, ["aac"] = Audio, ["ogg"] = Audio, ["oga"] = Audio,
        ["opus"] = Audio, ["m4a"] = Audio, ["wma"] = Audio, ["ape"] = Audio, ["aiff"] = Audio, ["aif"] = Audio,
        ["mid"] = Audio, ["midi"] = Audio, ["amr"] = Audio,
        // 视频
        ["mp4"] = Video, ["mkv"] = Video, ["avi"] = Video, ["mov"] = Video, ["wmv"] = Video, ["flv"] = Video,
        ["webm"] = Video, ["m4v"] = Video, ["mpg"] = Video, ["mpeg"] = Video, ["3gp"] = Video,
        ["rm"] = Video, ["rmvb"] = Video, ["vob"] = Video,
        // 压缩包
        ["zip"] = Archive, ["rar"] = Archive, ["7z"] = Archive, ["tar"] = Archive, ["gz"] = Archive,
        ["bz2"] = Archive, ["xz"] = Archive, ["tgz"] = Archive, ["tbz"] = Archive, ["tbz2"] = Archive,
        ["tzst"] = Archive, ["lz"] = Archive, ["lz4"] = Archive, ["zst"] = Archive, ["cab"] = Archive,
        ["jar"] = Archive, ["war"] = Archive, ["ear"] = Archive, ["iso"] = Archive, ["lzma"] = Archive,
        // 程序与安装包
        ["exe"] = Program, ["msi"] = Program, ["dll"] = Program, ["so"] = Program, ["dylib"] = Program,
        ["sys"] = Program, ["com"] = Program, ["out"] = Program, ["bin"] = Program, ["run"] = Program,
        ["apk"] = Program, ["deb"] = Program, ["rpm"] = Program, ["appimage"] = Program, ["a"] = Program,
        ["lib"] = Program, ["node"] = Program,
        // 数据库文件
        ["db"] = Database, ["sqlite"] = Database, ["sqlite3"] = Database, ["sqlitedb"] = Database,
        ["mdb"] = Database, ["accdb"] = Database, ["dump"] = Database, ["myd"] = Database,
        ["myi"] = Database, ["frm"] = Database,
        // 文本
        ["txt"] = Text, ["log"] = Text, ["md"] = Text, ["markdown"] = Text, ["rst"] = Text, ["nfo"] = Text,
        ["text"] = Text,
        // 表格
        ["xls"] = Sheet, ["xlsx"] = Sheet, ["xlsm"] = Sheet, ["xlt"] = Sheet, ["csv"] = Sheet,
        ["tsv"] = Sheet, ["ods"] = Sheet,
        ["pdf"] = Pdf,
        // 文档与演示
        ["doc"] = Document, ["docx"] = Document, ["docm"] = Document, ["dot"] = Document, ["dotx"] = Document,
        ["odt"] = Document, ["ppt"] = Document, ["pptx"] = Document, ["pptm"] = Document, ["pps"] = Document,
        ["ppsx"] = Document, ["odp"] = Document, ["rtf"] = Document
    };

    // 没有扩展名的常见文件按完整文件名归类。
    private static readonly Dictionary<string, string> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        ["dockerfile"] = Code, ["makefile"] = Code, ["gemfile"] = Code, ["rakefile"] = Code,
        ["procfile"] = Code, ["vagrantfile"] = Code, ["jenkinsfile"] = Code, ["cmakelists.txt"] = Code,
        ["readme"] = Text, ["license"] = Text, ["licence"] = Text, ["changelog"] = Text,
        ["contributing"] = Text, ["notice"] = Text, ["authors"] = Text, ["copying"] = Text
    };

    public static string Category(string? name, bool isDirectory, bool isSymlink)
    {
        if (isSymlink) return Link;
        if (isDirectory) return Folder;
        var file = name?.Trim() ?? "";
        if (file.Length == 0) return File;
        var extension = Path.GetExtension(file);
        if (extension.Length > 1 && Extensions.TryGetValue(extension[1..], out var category)) return category;
        if (Names.TryGetValue(file, out var named)) return named;
        return File;
    }

    public static string Label(string category) => category switch
    {
        Folder => "文件夹", Link => "链接", Code => "代码", Config => "配置", Image => "图片",
        Audio => "音频", Video => "视频", Archive => "压缩包", Text => "文本", Sheet => "表格",
        Program => "程序", Database => "数据库", Pdf => "PDF", Document => "文档", _ => "文件"
    };
}