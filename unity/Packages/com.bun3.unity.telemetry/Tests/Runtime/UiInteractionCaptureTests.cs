using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Bun3.Unity.Telemetry.Tests
{
    public class UiInteractionCaptureTests
    {
        sealed class CaptureSink : ITelemetrySink
        {
            public readonly List<TelemetryRecord> Written = new();
            public bool Remote => false;
            // Engine log records from earlier tests may arrive mid-run; only UI records are counted.
            public void Write(TelemetryRecord record)
            {
                if (record.Category == TelemetryCategory.Ui) Written.Add(record);
            }
            public void Tick(double now) { }
            public UniTask FlushAsync(TimeSpan timeout) => UniTask.CompletedTask;
        }

        TelemetryHub previousHub;
        CaptureSink sink;
        GameObject eventSystem, canvas, capture;
        Transform panel;

        [SetUp]
        public void SetUp()
        {
            previousHub = Telemetry.Hub;
            sink = new CaptureSink();
            Telemetry.Hub = new TelemetryBuilder(null).Add(sink, TelemetryTier.Debug).Build();
            UiControlTracker.ResetBurst();
            eventSystem = new GameObject("TestEventSystem", typeof(EventSystem));
            canvas = new GameObject("TestCanvas", typeof(Canvas), typeof(GraphicRaycaster));
            panel = new GameObject("Panel", typeof(RectTransform)).transform;
            panel.SetParent(canvas.transform, false);
            capture = new GameObject("TestCapture", typeof(UiInteractionCapture));
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.Destroy(capture);
            UnityEngine.Object.Destroy(canvas);
            UnityEngine.Object.Destroy(eventSystem);
            UiControlTracker.ResetBurst();
            Telemetry.Hub = previousHub;
        }

        GameObject Make(string name, params Type[] components)
        {
            var go = new GameObject(name, components.Prepend(typeof(RectTransform)).ToArray());
            go.transform.SetParent(panel, false);
            return go;
        }

        void Scan() => capture.GetComponent<UiInteractionCapture>().ScanNow();

        static void Click(GameObject go) =>
            ExecuteEvents.Execute(go, new PointerEventData(EventSystem.current), ExecuteEvents.pointerClickHandler);

        static object Prop(TelemetryRecord r, string key) => r.Props.First(p => p.Key == key).Value;

        [UnityTest]
        public IEnumerator RuntimeButtonClickIsRecordedWithPath()
        {
            var go = Make("OkButton(Clone)", typeof(Image), typeof(Button));
            yield return null;
            Scan();
            Click(go);
            Assert.That(sink.Written, Has.Count.EqualTo(1));
            var r = sink.Written[0];
            Assert.That(r.Name, Is.EqualTo("ui_click"));
            Assert.That(r.Category, Is.EqualTo(TelemetryCategory.Ui));
            Assert.That(Prop(r, "kind"), Is.EqualTo("button"));
            Assert.That(Prop(r, "path"), Is.EqualTo("TestCanvas/Panel/OkButton"));
            Assert.That(Prop(r, "interactable"), Is.EqualTo(true));
        }

        [UnityTest]
        public IEnumerator ToggleRecordsValueAfterChange()
        {
            var go = Make("MuteToggle", typeof(Image), typeof(Toggle));
            yield return null;
            Scan();
            Assert.That(go.GetComponents<Component>().Last(), Is.TypeOf<UiControlTracker>(), "the tracker is attached after the control");
            Click(go);
            Assert.That(sink.Written, Has.Count.EqualTo(1));
            Assert.That(Prop(sink.Written[0], "kind"), Is.EqualTo("toggle"));
            Assert.That(Prop(sink.Written[0], "value"), Is.EqualTo(true));
        }

        [UnityTest]
        public IEnumerator SliderRecordsOnRelease()
        {
            var go = Make("VolumeSlider", typeof(Slider));
            var slider = go.GetComponent<Slider>();
            yield return null;
            Scan();
            slider.value = 0.75f;
            Assert.That(sink.Written, Is.Empty);
            ExecuteEvents.Execute(go, new PointerEventData(EventSystem.current), ExecuteEvents.pointerUpHandler);
            Assert.That(sink.Written, Has.Count.EqualTo(1));
            Assert.That(sink.Written[0].Name, Is.EqualTo("ui_slider"));
            Assert.That(Prop(sink.Written[0], "value"), Is.EqualTo(0.75f));
        }

        [UnityTest]
        public IEnumerator InputSubmitRecordsLengthNotText()
        {
            var go = Make("NameInput", typeof(Image), typeof(TMP_InputField));
            var input = go.GetComponent<TMP_InputField>();
            yield return null;
            Scan();
            input.onSubmit.Invoke("secret");
            Assert.That(sink.Written, Has.Count.EqualTo(1));
            var r = sink.Written[0];
            Assert.That(r.Name, Is.EqualTo("ui_input_submit"));
            Assert.That(Prop(r, "length"), Is.EqualTo(6));
            Assert.That(r.Props.Any(p => p.Value is string s && s.Contains("secret")), Is.False);
        }

        [UnityTest]
        public IEnumerator InputContentNeverRecorded()
        {
            var go = Make("NameInput", typeof(Image), typeof(TMP_InputField));
            var input = go.GetComponent<TMP_InputField>();
            yield return null;
            Scan();
            input.text = "secret-text";
            input.onSubmit.Invoke(input.text);
            Click(go);
            var submit = sink.Written.Single(r => r.Name == "ui_input_submit");
            Assert.That(Prop(submit, "length"), Is.EqualTo(11));
            Assert.That(sink.Written.Any(r => r.Props.Any(p => p.Value is string s && s.Contains("secret-text"))), Is.False);
            Assert.That(sink.Written.Any(r => TelemetryJson.RecordLine(r).Contains("secret-text")), Is.False);
        }

        [UnityTest]
        public IEnumerator ScrollbarGetsNoTracker()
        {
            var go = Make("ListScrollbar", typeof(Image), typeof(Scrollbar));
            yield return null;
            Scan();
            Assert.That(go.GetComponent<UiControlTracker>(), Is.Null, "scrollbar clicks must reach the parent");
        }

        [UnityTest]
        public IEnumerator DisabledButtonClickRecordsNotInteractable()
        {
            var go = Make("LockedButton", typeof(Image), typeof(Button));
            go.GetComponent<Button>().interactable = false;
            yield return null;
            Scan();
            Click(go);
            Assert.That(sink.Written, Has.Count.EqualTo(1));
            Assert.That(Prop(sink.Written[0], "interactable"), Is.EqualTo(false));
        }

        [UnityTest]
        public IEnumerator DropdownItemClickDoesNotLeakOptionText()
        {
            var go = TMP_DefaultControls.CreateDropdown(new TMP_DefaultControls.Resources());
            go.name = "ModeDropdown";
            go.transform.SetParent(panel, false);
            var dropdown = go.GetComponent<TMP_Dropdown>();
            dropdown.options = new List<TMP_Dropdown.OptionData>
            {
                new("first"), new("secret-option"), new("third"),
            };
            yield return null;
            Scan();
            dropdown.Show();
            yield return null;
            Scan();
            var item = dropdown.GetComponentsInChildren<Toggle>(true)
                .First(t => t.name.StartsWith("Item 1"));
            Click(item.gameObject);
            Assert.That(sink.Written.Any(r => r.Props.Any(p => p.Value is string s && s.Contains("secret-option"))), Is.False);
            var click = sink.Written.First(r => r.Name == "ui_click" && ((string)Prop(r, "path")).Contains("/Item "));
            Assert.That((string)Prop(click, "path"), Does.EndWith("Item 1"));
            var picked = sink.Written.Where(r => r.Name == "ui_dropdown").ToList();
            Assert.That(picked, Has.Count.EqualTo(1));
            Assert.That(Prop(picked[0], "index"), Is.EqualTo(1));
        }
    }
}
