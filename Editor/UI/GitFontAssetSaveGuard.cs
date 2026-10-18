using System;
using System.Linq;
using UnityEditor;

namespace GitVersionControl
{
    /// <summary>
    /// 패키지 글꼴 폴더(<see cref="GitFonts.FontFolder"/>)의 글꼴 에셋을 저장하지 않게 한다.
    /// 글꼴 에셋은 동적(Dynamic)이라 에디터가 새 글자를 그릴 때마다 아틀라스가 바뀌는데, 그대로 두면 프로젝트를 저장할 때마다
    /// 에셋 파일이 달라져 커밋마다 변경이 생긴다. 바뀐 아틀라스는 메모리에만 두고, 에디터를 다시 켜면 다시 채운다.
    /// 처음 에셋을 만들거나 설정(대체 글꼴 순서 등)을 바꿀 때만 <see cref="AllowSave"/> 를 켜서 저장한다.
    /// </summary>
    public sealed class GitFontAssetSaveGuard : AssetModificationProcessor
    {
        /// <summary>
        /// 글꼴 에셋을 일부러 저장할 때만 잠깐 켠다.
        /// </summary>
        public static bool AllowSave;

        private static string[] OnWillSaveAssets(string[] paths)
        {
            if (AllowSave)
            {
                return paths;
            }
            return paths.Where(path => path.StartsWith(GitFonts.FontFolder, StringComparison.Ordinal) == false).ToArray();
        }
    }
}
