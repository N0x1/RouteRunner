using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using ModMenu.Api;
using ModMenu.Behaviours.OptionList.ValueControllers;
using RouteRunner.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RouteRunner
{
    public sealed partial class Plugin
    {
        // JIT this method only after the optional dependency has been detected. No ModMenu types are stored on Plugin.
        [MethodImpl(MethodImplOptions.NoInlining)]
        void ConfigureModMenu()
        {
            ModMenuCustomisation.SetPluginDescription("Movement ghosts for exploration practice.");
            ModMenuCustomisation.RegisterContentBuilder(context =>
            {
                try { BuildModSettings(context); }
                catch (Exception error)
                {
                    modMenuReady = false;
                    context.AppendTextBox("Settings integration failed. Use Route Runner's F6 Settings tab. See LogOutput.log.");
                    Logger.LogError(error);
                }
            });
            // Mod Menu 1.2.0 does not invoke custom builders unless a native config control exists.
            // Keep real native settings, including the master switches, rather than adding a dummy entry.
            ModMenuCustomisation.HideEntries(Config.Select(pair => pair.Value).Where(entry => entry != sampleRate && entry != modEnabled && entry != showHud));
            sampleRate.SettingChanged += (_, __) => QueueModSettingsSave();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        void BuildModSettings(OptionListContext context)
        {
            Compact(context.AppendHeader("Keys"));
            var feedback = Compact(context.AppendTextBox("Escape cancels rebinding."));
            feedback.FontSize = 14;
            foreach (var pair in new[] { ("Panel", menuKey), ("Record / save", recordKey),
                ("Play / restart", playKey), ("Stop", stopKey), ("Return to start", resetKey) })
            {
                var binding = pair.Item2;
                Compact(context.AppendKeyCodeInput(pair.Item1, () => LegacyKey(binding.Value), code =>
                {
                    if (code == KeyCode.None || code == KeyCode.Escape) { feedback.Text = "Key change cancelled."; return; }
                    if (!Enum.TryParse(KeyNames.InputSystem(code.ToString()), true, out Key key) || !Enum.IsDefined(typeof(Key), key) || key == Key.None ||
                        (Keyboard.current != null && !Keyboard.current.allKeys.Any(k => k.keyCode == key)))
                    { feedback.Text = "Unsupported key."; return; }
                    if (new[] { menuKey, recordKey, playKey, stopKey, resetKey }.Any(b => b != binding && b.Value == key))
                    { feedback.Text = key + " is already assigned."; return; }
                    binding.Value = key;
                    suppressHotkeysThroughFrame = Time.frameCount + 1;
                    QueueModSettingsSave();
                    feedback.Text = pair.Item1 + " is now " + key + ".";
                }));
            }
            Compact(context.AppendHeader("Ghost"));
            var appearance = new List<BoxedValueController>();
            Action refresh = () => { foreach (var controller in appearance) controller.UpdateAppearance(); };
            var colourControl = Compact(context.AppendColorInput("Colour", () => GhostColour, colour =>
            {
                ApplyModColour(colour); refresh();
            }));
            appearance.Add(colourControl);
            // Replace only this input's cloned event so native parsing cannot discard bare hex codes.
            colourControl.inputField.onEndEdit = new TMPro.TMP_InputField.SubmitEvent();
            colourControl.inputField.onEndEdit.AddListener(value =>
            {
                if (HexColour.TryParse(value, out uint rgba, out bool hasAlpha))
                {
                    ApplyModColour(new Color((rgba >> 24) / 255f, ((rgba >> 16) & 255) / 255f,
                        ((rgba >> 8) & 255) / 255f, hasAlpha ? (rgba & 255) / 255f : opacity.Value));
                    feedback.Text = "Colour saved.";
                }
                else feedback.Text = "Invalid hex colour. Previous colour kept.";
                refresh();
            });
            appearance.Add(AppendModSlider<float>(context, "Transparency (%)", () => (1 - opacity.Value) * 100, value =>
            {
                SetSetting(opacity, 1 - CleanUnit(value / 100)); QueueModSettingsSave(); refresh();
            }, 0, 100));
            Compact(context.AppendButton("", "Reset colour", () => { ApplyModColour(new Color(.15f, .95f, 1, opacity.Value)); refresh(); }));
            AppendModSlider<float>(context, "Countdown (s)", () => countdownSeconds.Value, value =>
            {
                SetSetting(countdownSeconds, CleanRange(value, 0, 10)); QueueModSettingsSave();
            }, 0, 10);
            Compact(context.AppendHeader("Practice"));
            AppendModSlider<int>(context, "Max recording (s)", () => maxSeconds.Value, value =>
            {
                maxSeconds.Value = Mathf.Clamp(value, 10, 300); QueueModSettingsSave();
            }, 10, 300);
            AppendModSlider<float>(context, "Panel size (%)", () => interfaceScale.Value * 100, value =>
            {
                SetSetting(interfaceScale, CleanRange(value / 100, .8f, 1.5f)); QueueModSettingsSave();
            }, 80, 150);
            Compact(context.AppendCheckbox("Idle hotkey hint", () => showIdleHint.Value, value =>
            {
                showIdleHint.Value = value; QueueModSettingsSave();
            }));
        }

        static NumericSliderValueController AppendModSlider<T>(OptionListContext context, string label, Func<T> getter, Action<T> setter, float min, float max)
            where T : struct, IComparable, IComparable<T>, IConvertible, IEquatable<T>, IFormattable
        {
            var control = Compact(context.AppendNumericSlider(label, getter, setter, min, max));
            // Mod Menu 1.2.0 only sets m_boundsSet for native config sliders. Its custom-slider
            // callback therefore ignores drags. Replace the event on our own instance only.
            control.slider.onValueChanged = new UnityEngine.UI.Slider.SliderEvent();
            control.slider.onValueChanged.AddListener(value =>
            {
                setter((T)Convert.ChangeType(value, typeof(T)));
                control.UpdateAppearance();
            });
            // SetupFromValues runs before the API assigns bounds; resync with the final range.
            control.UpdateAppearance();
            return control;
        }

        // Change only the controls created here; never resize Mod Menu's prefabs or other mods' UI.
        static T Compact<T>(T item) where T : Component
        {
            foreach (var text in item.GetComponentsInChildren<TMPro.TMP_Text>(true))
            {
                text.enableAutoSizing = false;
                text.fontSize = Mathf.Min(text.fontSize > 0 ? text.fontSize : 18, 18);
            }
            return item;
        }

        static KeyCode LegacyKey(Key key) => Enum.TryParse(KeyNames.Legacy(key.ToString()), true, out KeyCode code) ? code : KeyCode.None;
        static float CleanUnit(float value) => CleanRange(value, 0, 1);
        static float CleanRange(float value, float min, float max) => float.IsNaN(value) || float.IsInfinity(value) ? min : Mathf.Clamp(value, min, max);
        void ApplyModColour(Color colour)
        {
            SetSetting(ghostRed, CleanUnit(colour.r)); SetSetting(ghostGreen, CleanUnit(colour.g));
            SetSetting(ghostBlue, CleanUnit(colour.b)); SetSetting(opacity, CleanUnit(colour.a));
            QueueModSettingsSave();
        }
        void QueueModSettingsSave()
        {
            settingsDirty = true; modSettingsSaveAt = Time.unscaledTime + .35f;
            ghost?.SetColour(GhostColour);
        }
    }
}
