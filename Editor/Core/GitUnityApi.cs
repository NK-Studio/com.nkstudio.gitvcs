using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Threading;

namespace GitVersionControl
{
    /// <summary>
    /// C# 문법 색칠(<see cref="GitSyntax"/>)이 아는 Unity API 이름 표.
    /// <list type="bullet">
    /// <item>타입: 에디터에 올라온 UnityEngine*·UnityEditor* 어셈블리의 공개 최상위 타입(약 3,600개)과 자주 쓰는 C# 기본 타입. 처음 쓸 때 한 번 훑는다(수십 ms).</item>
    /// <item>흔한 이름은 뺀다: .NET 의 속성·메서드 이름과 같은 이름(Length·Position·State 등)과 <see cref="CommonNames"/>.
    /// 사용자 코드의 속성·변수 이름을 Unity 타입으로 잘못 칠하지 않으려는 것이다. 단 <see cref="CoreNames"/>(Debug·Time 등)는 남긴다.</item>
    /// <item>어트리뷰트: [ ] 안에서만 쓰는 이름 표. 그 자리는 어트리뷰트만 올 수 있어 흔한 이름(Range 등)도 남긴다.</item>
    /// <item>메시지: Awake·Update 같은 Unity 메시지 메서드 이름(선언 자리에서만 칠한다).</item>
    /// </list>
    /// </summary>
#if UNITY_6000_7_OR_NEWER
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
    public static class GitUnityApi
    {
        private sealed class Tables
        {
            public readonly HashSet<string> Types = new(StringComparer.Ordinal);
            public readonly HashSet<string> Attributes = new(StringComparer.Ordinal);
            public readonly HashSet<string> Namespaces = new(StringComparer.Ordinal);
        }

        private static readonly Lazy<Tables> s_tables = new(Build, LazyThreadSafetyMode.ExecutionAndPublication);

        /// <summary>
        /// 자주 쓰는 C# 기본 라이브러리 타입. 리플렉션으로 고르면 흔한 이름이 너무 많이 섞여 손으로 고른다.
        /// </summary>
        private static readonly string[] CSharpTypes =
        {
            "List", "Dictionary", "HashSet", "Queue", "Stack", "LinkedList", "SortedDictionary", "SortedList", "SortedSet", "KeyValuePair",
            "IEnumerable", "IEnumerator", "ICollection", "IList", "IDictionary", "ISet", "IReadOnlyList", "IReadOnlyCollection", "IReadOnlyDictionary",
            "IComparer", "IEqualityComparer", "IComparable", "IEquatable", "IDisposable", "IAsyncDisposable", "IAsyncEnumerable",
            "Action", "Func", "Predicate", "Comparison", "EventHandler", "EventArgs",
            "Task", "ValueTask", "CancellationToken", "CancellationTokenSource",
            "Exception", "ArgumentException", "ArgumentNullException", "ArgumentOutOfRangeException", "InvalidOperationException",
            "NotImplementedException", "NotSupportedException", "NullReferenceException", "IndexOutOfRangeException", "KeyNotFoundException",
            "ObjectDisposedException", "OperationCanceledException", "FormatException", "TimeoutException",
            "StringBuilder", "Math", "MathF", "Span", "ReadOnlySpan", "Memory", "ReadOnlyMemory",
            "DateTime", "DateTimeOffset", "TimeSpan", "Guid", "Tuple", "ValueTuple", "Nullable", "Enum", "Convert", "BitConverter",
            "Stopwatch", "Regex", "Encoding", "StringComparer", "StringComparison", "EqualityComparer", "Comparer",
            "Lazy", "WeakReference", "Interlocked", "Enumerable", "Array", "Activator", "IntPtr", "UIntPtr", "Console", "Environment",
        };

        /// <summary>
        /// C#·.NET 쪽 어트리뷰트 가운데 Unity 코드에 흔한 것.
        /// </summary>
        private static readonly string[] CSharpAttributes =
        {
            "Serializable", "NonSerialized", "Obsolete", "Flags", "Conditional", "MethodImpl", "StructLayout", "FieldOffset",
            "SerializableAttribute", "NonSerializedAttribute", "ObsoleteAttribute", "FlagsAttribute",
        };

        /// <summary>
        /// 리플렉션으로 넣지 않는 네임스페이스 조각(System.Collections.Generic.List 의 List 를 칠하려고).
        /// </summary>
        private static readonly string[] CSharpNamespaces =
        {
            "System", "Collections", "Generic", "Concurrent", "Threading", "Tasks", "Text", "RegularExpressions", "Linq", "IO", "Diagnostics",
        };

        /// <summary>
        /// 흔해서 빼는 이름. .NET 멤버 이름과 겹치는 것은 런타임에 따로 고르고, 여기에는 그 검사로 안 걸리지만
        /// 사용자 코드에서 속성·변수 이름으로 흔한 것을 둔다(Unity 6000.7 XML 문서 기준으로 고른 목록 포함).
        /// </summary>
        private static readonly string[] CommonNames =
        {
            "Appearance", "Arguments", "Arm", "Assembly", "AssemblyFlags", "Background", "Block", "Box", "ChannelInfo", "Client", "Column", "Columns",
            "Comment", "Condition", "Config", "Configuration", "Connection", "Constant", "Constants", "Content", "Context", "Count", "Data", "DataType",
            "Device", "Direction", "Directory", "Element", "Error", "ErrorCode", "Event", "EventID", "Events", "EventType", "Extensions", "Feature",
            "File", "FileStatus", "Filter", "FilterAttribute", "FilterType", "Flags", "Format", "Formatting", "Hand", "Help", "Id", "Image", "Index",
            "Info", "Item", "Items", "Key", "Kind", "Label", "Layout", "Length", "Level", "Locale", "Locality", "Location", "LogType", "Loop",
            "MainModule", "Manual", "Mask", "Menu", "Message", "MessageType", "Mode", "Name", "Node", "Notification", "ObjectFlags", "Offset",
            "Operation", "Options", "OSArchitecture", "Particle", "Path", "Ping", "Pipe", "Position", "Priority", "Properties", "Property",
            "PropertyName", "Provider", "Query", "Range", "Reader", "ReadOnly", "ReadWrite", "Repeat", "Report", "Request", "ResolveMethod",
            "Response", "Result", "Rotation", "Sample", "Scale", "Security", "Selector", "Serializable", "Server", "Settings", "Severity", "Size",
            "Snapshot", "SortDirection", "SortOrder", "Source", "Space", "State", "Status", "StatusCode", "Style", "System", "Tab", "Target", "Text",
            "TicksPerSecond", "Tool", "Translate", "Type", "Types", "Unicode", "Update", "UploadData", "UserState", "Value", "ValueType", "Variable",
            "Variant", "Version", "Visibility", "WarningLevel", "Window", "Windows", "Writer", "X86",
        };

        /// <summary>
        /// 흔한 이름이어도 Unity 코드에서 거의 늘 Unity·C# 타입으로 쓰는 것.
        /// </summary>
        private static readonly string[] CoreNames =
        {
            "Application", "Color", "Component", "Debug", "Input", "Object", "Random", "Resources", "Selection", "Task", "Time", "Transform", "Undo",
        };

        /// <summary>
        /// Unity 가 이름으로 부르는 메시지 메서드(MonoBehaviour·ScriptableObject·Editor·EditorWindow·직렬화 콜백).
        /// </summary>
        private static readonly HashSet<string> Messages = new(StringComparer.Ordinal)
        {
            "Awake", "Start", "Update", "FixedUpdate", "LateUpdate", "OnEnable", "OnDisable", "OnDestroy", "OnValidate", "Reset", "OnGUI",
            "OnDrawGizmos", "OnDrawGizmosSelected", "OnApplicationFocus", "OnApplicationPause", "OnApplicationQuit",
            "OnBecameVisible", "OnBecameInvisible", "OnWillRenderObject", "OnRenderObject", "OnPreCull", "OnPreRender", "OnPostRender", "OnRenderImage",
            "OnCollisionEnter", "OnCollisionStay", "OnCollisionExit", "OnCollisionEnter2D", "OnCollisionStay2D", "OnCollisionExit2D",
            "OnTriggerEnter", "OnTriggerStay", "OnTriggerExit", "OnTriggerEnter2D", "OnTriggerStay2D", "OnTriggerExit2D",
            "OnControllerColliderHit", "OnJointBreak", "OnJointBreak2D", "OnParticleCollision", "OnParticleTrigger", "OnParticleSystemStopped",
            "OnMouseDown", "OnMouseUp", "OnMouseUpAsButton", "OnMouseDrag", "OnMouseEnter", "OnMouseExit", "OnMouseOver",
            "OnAnimatorIK", "OnAnimatorMove", "OnAudioFilterRead", "OnTransformChildrenChanged", "OnTransformParentChanged",
            "OnBeforeTransformParentChanged", "OnRectTransformDimensionsChange", "OnCanvasGroupChanged", "OnDidApplyAnimationProperties",
            "OnInspectorGUI", "OnSceneGUI", "OnPreviewGUI", "OnInteractivePreviewGUI", "OnPreviewSettings", "OnHeaderGUI", "CreateInspectorGUI",
            "HasPreviewGUI", "RenderStaticPreview", "CreateGUI", "OnFocus", "OnLostFocus", "OnHierarchyChange", "OnProjectChange",
            "OnSelectionChange", "OnInspectorUpdate", "ShowButton", "OnBeforeSerialize", "OnAfterDeserialize",
        };

        /// <summary>
        /// Unity·C# 타입 이름인지(흔한 이름은 뺀 표).
        /// </summary>
        public static bool IsType(string name)
        {
            return string.IsNullOrEmpty(name) == false && s_tables.Value.Types.Contains(name);
        }

        /// <summary>
        /// [ ] 안에 올 수 있는 어트리뷰트 이름인지(SerializeField·Header·HeaderAttribute·Range).
        /// </summary>
        public static bool IsAttribute(string name)
        {
            return string.IsNullOrEmpty(name) == false && s_tables.Value.Attributes.Contains(name);
        }

        /// <summary>
        /// Unity 메시지 메서드 이름인지(Awake·Update·OnTriggerEnter).
        /// </summary>
        public static bool IsMessage(string name)
        {
            return string.IsNullOrEmpty(name) == false && Messages.Contains(name);
        }

        /// <summary>
        /// UnityEngine·SceneManagement·Generic 처럼 네임스페이스 조각인지. UnityEngine.Debug 의 Debug 를 칠할지 고를 때 쓴다.
        /// </summary>
        public static bool IsNamespaceSegment(string name)
        {
            return string.IsNullOrEmpty(name) == false && s_tables.Value.Namespaces.Contains(name);
        }

        /// <summary>
        /// 도메인 로드 직후 백그라운드에서 표를 미리 만든다. 처음 .cs 를 칠할 때 기다리지 않게.
        /// </summary>
        [UnityEditor.InitializeOnLoadMethod]
        private static void WarmUp()
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    _ = s_tables.Value;
                }
                catch (Exception)
                {
                    // 칠하기용 표라 실패해도 색만 덜 칠해진다. 쓰는 쪽에서 다시 던지면 그때 드러난다.
                }
            });
        }

        private static Tables Build()
        {
            Tables tables = new();
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.IsDynamic || IsUnityName(assembly.GetName().Name) == false)
                {
                    continue;
                }
                foreach (Type type in ExportedTypes(assembly))
                {
                    if (type.IsNested || IsUnityNamespace(type.Namespace) == false || IsHidden(type))
                    {
                        continue;
                    }
                    foreach (string segment in type.Namespace.Split('.'))
                    {
                        tables.Namespaces.Add(segment);
                    }
                    string name = SimpleName(type.Name);
                    if (name.Length < 3)
                    {
                        continue;
                    }
                    tables.Types.Add(name);
                    if (typeof(Attribute).IsAssignableFrom(type))
                    {
                        tables.Attributes.Add(name);
                        const string suffix = "Attribute";
                        if (name.Length > suffix.Length && name.EndsWith(suffix, StringComparison.Ordinal))
                        {
                            tables.Attributes.Add(name.Substring(0, name.Length - suffix.Length));
                        }
                    }
                }
            }
            // .NET 멤버 이름과 겹치는지는 Unity 이름에만 본다. C# 기본 타입은 손으로 고른 목록이라(Exception·Comparer 도 멤버 이름이다) 그대로 둔다.
            HashSet<string> common = new(CommonNames, StringComparer.Ordinal);
            HashSet<string> members = DotNetMemberNames();
            common.UnionWith(tables.Types.Where(members.Contains));
            common.ExceptWith(CoreNames);
            tables.Types.ExceptWith(common);
            tables.Types.UnionWith(CSharpTypes);
            tables.Attributes.UnionWith(CSharpAttributes);
            tables.Namespaces.UnionWith(CSharpNamespaces);
            return tables;
        }

        /// <summary>
        /// .NET 기본 어셈블리의 공개 멤버 이름(PascalCase). 이와 같은 Unity 타입 이름은 사용자 코드의 속성 이름과도 겹치기 쉽다.
        /// </summary>
        private static HashSet<string> DotNetMemberNames()
        {
            HashSet<string> names = new(StringComparer.Ordinal);
            Assembly[] assemblies = { typeof(object).Assembly, typeof(Uri).Assembly, typeof(Enumerable).Assembly };
            foreach (Assembly assembly in assemblies.Distinct())
            {
                foreach (Type type in ExportedTypes(assembly))
                {
                    MemberInfo[] members;
                    try
                    {
                        members = type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);
                    }
                    catch (Exception)
                    {
                        continue;
                    }
                    foreach (MemberInfo member in members)
                    {
                        string name = member.Name;
                        if (name.Length > 0 && char.IsUpper(name[0]) && name.IndexOf('_') < 0 && name.IndexOf('.') < 0)
                        {
                            names.Add(name);
                        }
                    }
                }
            }
            return names;
        }

        private static IEnumerable<Type> ExportedTypes(Assembly assembly)
        {
            try
            {
                return assembly.GetExportedTypes();
            }
            catch (ReflectionTypeLoadException exception)
            {
                return exception.Types.Where(type => type != null && type.IsPublic);
            }
            catch (Exception)
            {
                return Array.Empty<Type>();
            }
        }

        private static bool IsUnityName(string name)
        {
            return name != null && (name.StartsWith("UnityEngine", StringComparison.Ordinal) || name.StartsWith("UnityEditor", StringComparison.Ordinal));
        }

        private static bool IsUnityNamespace(string ns)
        {
            return IsUnityName(ns)
                   && ns.IndexOf("Internal", StringComparison.Ordinal) < 0
                   && ns.IndexOf("Experimental", StringComparison.Ordinal) < 0
                   && ns.IndexOf("PlayerLoop", StringComparison.Ordinal) < 0;
        }

        /// <summary>
        /// 자동 완성에서 숨긴 타입([EditorBrowsable(Never)])은 사용자 코드에 거의 안 나온다.
        /// </summary>
        private static bool IsHidden(Type type)
        {
            try
            {
                EditorBrowsableAttribute browsable = type.GetCustomAttribute<EditorBrowsableAttribute>(false);
                return browsable != null && browsable.State == EditorBrowsableState.Never;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static string SimpleName(string name)
        {
            int tick = name.IndexOf('`');
            return tick < 0 ? name : name.Substring(0, tick);
        }
    }
}
