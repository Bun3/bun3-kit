using System.Collections.Generic;
using UnityEngine;

namespace Bun3.Core.UnifiedToggle
{
    // Editor-only dialog helper. Defers display to the next editor tick, so it's safe to call
    // from OnValidate / OnInspectorGUI without disturbing serialization or GUI state.
    // No-op in player builds. All dialog strings are centralized here so call sites stay
    // literal-free.
    public static class UnifiedToggleDialog
    {
        private const string TitleInvalidAuthorGroup = "Invalid Author Group";
        private const string TitleInvalidToggleRegistration = "Invalid Toggle Registration";
        private const string TitleToggleDropped = "Toggle Dropped";
        private const string TitleAuthorGroupRequired = "Author Group Required";

        private const string HierarchyRule =
            "A toggle's UnifiedToggleGroup must be placed on a strict ancestor Transform.";

        private const string AuthorGroupRequiredRule =
            "This toggle requires an Author Group on an ancestor Transform to function. Standalone toggles are not supported.";

        private const string Separator = "\n\n";
        private const string OkButton = "OK";

        public static void Show(string title, string body)
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.delayCall += () =>
            {
                UnityEditor.EditorUtility.DisplayDialog(title, body, OkButton);
            };
#endif
        }

        public static void ShowAuthorGroupCleared(string toggleName, string groupName)
        {
            Show(TitleInvalidAuthorGroup,
                $"'{toggleName}'._authorGroup='{groupName}' is not an ancestor Transform and will be cleared." +
                Separator + HierarchyRule);
        }

        public static void ShowAuthorGroupAssignmentRefused(string toggleName, string groupName)
        {
            Show(TitleInvalidAuthorGroup,
                $"Refused to set _authorGroup='{groupName}': not an ancestor Transform of '{toggleName}'." +
                Separator + HierarchyRule);
        }

        public static void ShowAuthorGroupHierarchyHint()
        {
            Show(TitleInvalidAuthorGroup, HierarchyRule);
        }

        public static void ShowToggleRegistrationRefused(string groupName, string toggleName)
        {
            Show(TitleInvalidToggleRegistration,
                $"'{groupName}' refused to register '{toggleName}': toggle Transform is not a descendant." +
                Separator + HierarchyRule);
        }

        public static void ShowTogglePruned(string groupName, string toggleName)
        {
            Show(TitleToggleDropped,
                $"'{groupName}' dropped '{toggleName}' from _toggles: not a descendant Transform." +
                Separator + HierarchyRule);
        }

        public static void ShowAuthorGroupRequired(string toggleName)
        {
            Show(TitleAuthorGroupRequired,
                $"'{toggleName}' has no Author Group assigned." +
                Separator + AuthorGroupRequiredRule);
        }
    }


    [ExecuteAlways]
    public abstract partial class BaseUnifiedToggle : MonoBehaviour, IUnifiedToggle
    {
        [SerializeField] protected UnifiedToggleGroup _authorGroup;

        public const string PropertyNameAuthorGroup = nameof(_authorGroup);

        public abstract void SetValue(string value);

        partial void OnDestroyEditor();

        protected virtual void OnDestroy()
        {
#if UNITY_EDITOR
            OnDestroyEditor();
#endif
            if (_authorGroup)
                _authorGroup.EnsureValidToggles();
        }

        public abstract void SetOptionValues(string[] values);

        protected virtual void OnEnable()
        {
            if (_authorGroup)
                _authorGroup.Register(this);
        }

        protected virtual void OnDisable()
        {
            if (_authorGroup)
                _authorGroup.Unregister(this);
        }

        // Constrains the cascade graph to a subgraph of the Transform tree so cycles are
        // structurally impossible. Returns true only when candidate is a strict ancestor of
        // target. Same Transform (self) returns false — self-authoring has no semantic and
        // would blur cascade direction.
        public static bool IsStrictAncestor(Transform candidate, Transform target)
        {
            if (candidate == null || target == null) return false;
            if (candidate == target) return false;
            return target.IsChildOf(candidate);
        }

        protected void ValidateAuthorGroupHierarchy()
        {
            if (_authorGroup == null) return;
            if (IsStrictAncestor(_authorGroup.transform, transform)) return;

            UnifiedToggleDialog.ShowAuthorGroupCleared(name, _authorGroup.name);
            _authorGroup = null;
        }
    }

    [DisallowMultipleComponent]
    public abstract partial class BaseUnifiedToggle<TComponent> : BaseUnifiedToggle where TComponent : Component
    {
        public abstract TComponent Component { get; }

        [SerializeReference, SubclassSelector] protected List<IUnifiedOption<TComponent>> _options = new();
        
        public const string PropertyNameOptions = nameof(_options);
        
        public IReadOnlyList<IUnifiedOption<TComponent>> Options => _options;

        // 자식 토글이 보유한 직렬화된 컴포넌트 참조를 채우는 hook.
        // GetComponent 호출은 자식이 자신이 보유한 필드명을 알고 있으므로 자식에서 수행.
        // component => this 형태(예: UnifiedToggleGameObject)라면 override 불필요.
        protected virtual void EnsureComponent() { }

        protected virtual void Reset()
        {
            EnsureComponent();
        }

        protected virtual void OnValidate()
        {
            EnsureComponent();
            ValidateAuthorGroupHierarchy();
        }

        public sealed override void SetOptionValues(string[] values)
        {
            for (var i = 0; i < _options.Count; i++)
            {
                var option = _options[i];
                option?.SetOptionValues(values);
            }
        }

        public sealed override void SetValue(string value)
        {
            // Standalone toggles are not supported — cascade must originate from a group.
            if (_authorGroup == null) return;
            if (_options.Count == 0) return;

            foreach (var option in _options)
            {
                if (option == null) continue;
                option.SetValue(Component, value);
            }
        }
    }
}
