using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Bun3.Unity.Telemetry
{
    /// <summary>Records clicks, submits and values of one control as Ui records. Input text is never recorded, only its length.</summary>
    sealed class UiControlTracker : MonoBehaviour, IPointerClickHandler, ISubmitHandler, IPointerUpHandler
    {
        static ClickBurst Burst = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        internal static void ResetBurst() => Burst = new ClickBurst();

        Selectable selectable;
        TMP_Dropdown dropdown;
        TMP_InputField input;
        Slider slider;
        Toggle toggle;
        string path;

        string Path => path ??= UiPath.Of(transform);

        void Awake()
        {
            selectable = GetComponent<Selectable>();
            dropdown = selectable as TMP_Dropdown;
            input = selectable as TMP_InputField;
            slider = selectable as Slider;
            toggle = selectable as Toggle;
            if (dropdown != null) dropdown.onValueChanged.AddListener(OnDropdown);
            if (input != null) input.onSubmit.AddListener(OnSubmitText);
        }

        void OnDestroy()
        {
            if (dropdown != null) dropdown.onValueChanged.RemoveListener(OnDropdown);
            if (input != null) input.onSubmit.RemoveListener(OnSubmitText);
        }

        internal static void TickBurst(double now)
        {
            if (Burst.Tick(now, out var p, out var n)) EmitRepeat(p, n);
        }

        static void EmitRepeat(string p, int n) =>
            Telemetry.Track(TelemetryCategory.Ui, "ui_click_repeat", ("path", p), ("count", n));

        public void OnPointerClick(PointerEventData e)
        {
            if (selectable == null || slider != null) return; // Sliders are recorded on release only
            if (!Burst.Register(Path, Time.realtimeSinceStartupAsDouble, out var ended, out var n))
                return;
            if (ended != null) EmitRepeat(ended, n);
            var kind = selectable is Button ? "button" : toggle != null ? "toggle"
                : input != null ? "input" : dropdown != null ? "dropdown" : "other";
            if (toggle != null)
                Telemetry.Track(TelemetryCategory.Ui, "ui_click", ("kind", kind), ("path", Path),
                    ("interactable", selectable.interactable), ("value", toggle.isOn));
            else
                Telemetry.Track(TelemetryCategory.Ui, "ui_click", ("kind", kind), ("path", Path),
                    ("interactable", selectable.interactable));
        }

        public void OnSubmit(BaseEventData e)
        {
            // A keyboard submit is the same action as a click. Input fields record through onSubmit.
            if (selectable is Button || toggle != null) OnPointerClick(null);
        }

        public void OnPointerUp(PointerEventData e)
        {
            if (slider != null)
                Telemetry.Track(TelemetryCategory.Ui, "ui_slider", ("path", Path), ("value", slider.value));
        }

        void OnDropdown(int index) =>
            Telemetry.Track(TelemetryCategory.Ui, "ui_dropdown", ("path", Path), ("index", index));

        void OnSubmitText(string text) =>
            Telemetry.Track(TelemetryCategory.Ui, "ui_input_submit", ("path", Path), ("length", text?.Length ?? 0));
    }
}
