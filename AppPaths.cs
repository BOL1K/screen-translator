using System.IO;

// Файлы данных (словарь, настройки, скриншоты, лог) всегда должны лежать рядом с exe,
// а не в "текущей директории" процесса — та зависит от способа запуска (двойной клик,
// автозапуск из реестра при логоне и т.п.) и может оказаться где угодно.
static class AppPaths
{
    public static string Resolve(string relativeName) => Path.Combine(AppContext.BaseDirectory, relativeName);
}
