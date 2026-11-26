using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Assets.Scripts.UI;
using Assets.Scripts.UI.Controls;
using Jundroo.Juicy.Widgets;

namespace SP2ServerMod.UI
{
    // Creates the game's own controls (headers, text rows, toggles, sliders, buttons) inside a parent widget. The controls come from templates
    // the game defines in its "Xml/Styles" file. (Adapted from the Armor Mod's panel, which is known to work in the game.)
    internal sealed class PanelBuilder
    {
        // Which sections the player collapsed. Kept between openings of the panel.
        private static readonly Dictionary<string, bool> CollapsedSections = new Dictionary<string, bool>();

        public Widget Parent { get; private set; }

        public PanelBuilder(Widget parent)
        {
            Parent = parent;
        }

        private Widget Template(string templateId, string label = null, string tooltip = null, string unit = null)
        {
            List<XAttribute> attributes = new List<XAttribute>();
            if (label != null) attributes.Add(new XAttribute("label", label));
            if (!string.IsNullOrEmpty(tooltip)) attributes.Add(new XAttribute("tooltip", tooltip));
            if (unit != null) attributes.Add(new XAttribute("unit", unit));
            return Parent.Context.CreateWidgetFromTemplate(templateId, Parent, attributes);
        }

        // A clickable section title. Everything added after it, up to the next header, is hidden when the player collapses it.
        public void Header(string title, bool collapsed = false)
        {
            HeaderScript header = Template("control-header").GetComponentInChildren<HeaderScript>();
            header.LabelText = title;

            bool wasCollapsed;
            header.StartCollapsed = CollapsedSections.TryGetValue(title, out wasCollapsed) ? wasCollapsed : collapsed;
            header.CollapsedStateChanged += (object sender, HeaderScript.CollapsedStateChangedEventArgs e) =>
            {
                CollapsedSections[title] = e.IsCollapsed;
            };
        }

        public TextControl Text(string label, string value)
        {
            TextControl control = new TextControl(Template("control-text"));
            control.LabelText = label;
            control.ValueText.Text = value;
            return control;
        }

        // A paragraph: the text rows are one line high, so a long text is broken into lines of about `width` characters, one row each (the label
        // on the first). One long row would spill over the rows around it.
        public void Note(string label, string text, int width = 44)
        {
            string line = "";
            bool first = true;
            foreach (string word in text.Split(' '))
            {
                if (line.Length > 0 && line.Length + 1 + word.Length > width)
                {
                    Text(first ? label : "", line);
                    first = false;
                    line = word;
                }
                else line = line.Length == 0 ? word : line + " " + word;
            }
            if (line.Length > 0) Text(first ? label : "", line);
        }

        public ToggleControl Toggle(string label, string tooltip, bool value, Action<bool> changed)
        {
            ToggleControl control = new ToggleControl(Template("control-toggle", label, tooltip));
            control.LabelText = label;
            control.IsOn = value;   // set before listening, so it doesn't count as a change
            control.ValueChanged += (bool x) => Run(() => changed(x));
            return control;
        }

        // A row with a label and a box where text can be typed (the game's own "control-spinner-input-label" with a plain text box). changed runs
        // when the typing is finished (Enter, or the box loses the focus) and only if the text is different from what it was.
        public TMPro.TMP_InputField TextInput(string label, string tooltip, string value, int maxLength, Action<string> changed)
        {
            List<XAttribute> attributes = new List<XAttribute>
            {
                new XAttribute("label", label),
                new XAttribute("contentType", "Standard"),
                new XAttribute("placeholderText", "Enter text..."),
            };
            if (!string.IsNullOrEmpty(tooltip)) attributes.Add(new XAttribute("tooltip", tooltip));
            Widget row = Parent.Context.CreateWidgetFromTemplate("control-spinner-input-label", Parent, attributes);
            TMPro.TMP_InputField field = row != null ? row.GetComponentInChildren<TMPro.TMP_InputField>(true) : null;
            if (field == null) return null;
            field.text = value ?? "";
            field.characterLimit = maxLength;
            string typedFrom = field.text;
            field.onSelect.AddListener(text => typedFrom = field.text);
            field.onEndEdit.AddListener(text =>
            {
                if (text != typedFrom) Run(() => changed(text));
            });
            return field;
        }

        // The game's slider with a box beside it where the value can be typed in.
        // steps: how many positions the slider has (0 = smooth). changed: while dragging or typing. released: when the mouse is let go, or the typing is done.
        public SliderControl Slider(string label, string tooltip, float min, float max, int steps,
                                    float value, string unit, Func<float, string> format,
                                    Action<float> changed, Action<float> released)
        {
            SliderControl control = new SliderControl(Template("control-slider-text", label, tooltip, unit ?? ""));
            control.LabelText = label;
            control.SetRange(min, max, steps);
            control.ValueFormatter = format;
            control.SetValue(value);

            // Listen only after the first SetValue, or opening the panel would "change" the setting.
            control.OnValueChanged += (float oldValue, float x) => Run(() => changed(x));
            if (released != null)
            {
                control.OnRelease += (float oldValue, float x) => Run(() => released(x));
                if (control.ValueInput != null && control.ValueInput.Input != null)
                {
                    float typedFrom = value;
                    control.ValueInput.Input.onSelect.AddListener(text => typedFrom = control.Value);
                    control.ValueInput.Input.onEndEdit.AddListener(text =>
                    {
                        if (control.Value != typedFrom) Run(() => released(control.Value));
                    });
                }
            }
            return control;
        }

        // A row of buttons side by side.
        public void Buttons(IList<KeyValuePair<string, Action>> buttons)
        {
            Buttons(buttons, -1);
        }

        // The same, with the button at the index picked: drawn in the blue the game uses for what is on.
        public void Buttons(IList<KeyValuePair<string, Action>> buttons, int picked)
        {
            XElement row = new XElement("Row",
                new XAttribute("height", "48"),
                new XAttribute("spacing", "16"));
            int span = Math.Max(1, 12 / Math.Max(1, buttons.Count));

            int at = 0;
            foreach (KeyValuePair<string, Action> button in buttons)
            {
                row.Add(new XElement("Button",
                    new XAttribute("class", at++ == picked ? "btn btn-primary" : "btn btn-default"),
                    new XAttribute("colSpan", span),
                    new XElement("Text", new XAttribute("text", button.Key))));
            }

            List<ButtonWidget> created =
                Parent.Context.CreateWidget(row, Parent, Parent.Stylesheet).Widgets.OfType<ButtonWidget>().ToList();
            for (int i = 0; i < created.Count && i < buttons.Count; i++)
            {
                Action action = buttons[i].Value;
                created[i].Clicked += (Widget w) => Run(action);
            }
        }

        // A mistake in one control's code must not break the whole panel.
        private static void Run(Action action)
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                SP2ServerModPlugin.Log.LogError(e);
            }
        }
    }
}
