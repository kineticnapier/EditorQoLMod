using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;

namespace Kiner.ADOFAIEditorQoL.UI
{
    internal sealed class NativeDropdown
    {
        private readonly TweakableDropdown dropdown;
        private readonly List<string> values = new List<string>();
        private readonly List<string> labels = new List<string>();

        public event Action ValueChanged;

        public NativeDropdown(TweakableDropdown dropdown)
        {
            this.dropdown = dropdown ?? throw new ArgumentNullException("dropdown");
            NeutralizeSpecializedTemplate();
            this.dropdown.onValueChanged = delegate(TweakableDropdownItem item)
            {
                if (ValueChanged != null) ValueChanged();
            };
        }

        public bool IsOpen
        {
            get { return dropdown != null && dropdown.isShowingList; }
        }

        public int SelectedIndex
        {
            get
            {
                if (dropdown.selectedItem == null) return 0;
                int index = values.IndexOf(dropdown.selectedItem.value);
                return index < 0 ? 0 : index;
            }
            set
            {
                if (values.Count == 0) return;
                int index = Math.Max(0, Math.Min(value, values.Count - 1));
                SetValue(values[index]);
            }
        }

        public string SelectedValue
        {
            get
            {
                if (dropdown.selectedItem != null && !string.IsNullOrEmpty(dropdown.selectedItem.value))
                    return dropdown.selectedItem.value;
                return values.Count == 0 ? string.Empty : values[0];
            }
        }

        public void SetOptions(IEnumerable<string> options)
        {
            if (options == null)
            {
                SetOptions((IEnumerable<KeyValuePair<string, string>>)null);
                return;
            }
            SetOptions(options.Select(value => new KeyValuePair<string, string>(value, value)));
        }

        public void SetOptions(IEnumerable<KeyValuePair<string, string>> options)
        {
            string previous = SelectedValue;
            values.Clear();
            labels.Clear();

            if (options != null)
            {
                foreach (KeyValuePair<string, string> option in options)
                {
                    if (string.IsNullOrWhiteSpace(option.Key) || values.Contains(option.Key)) continue;
                    values.Add(option.Key);
                    labels.Add(string.IsNullOrWhiteSpace(option.Value) ? option.Key : option.Value);
                }
            }

            if (values.Count == 0)
            {
                values.Add("None");
                labels.Add("なし");
            }

            NeutralizeSpecializedTemplate();
            dropdown.HideList();
            dropdown.selectedItem = null;
            dropdown.items.Clear();
            dropdown.itemValues.Clear();
            dropdown.customItemValues.Clear();
            if (dropdown.customLabels == null) dropdown.customLabels = new List<string>();
            else dropdown.customLabels.Clear();

            dropdown.itemValues.AddRange(values);
            dropdown.customItemValues.AddRange(values);
            dropdown.customLabels.AddRange(labels);
            dropdown.useCustomLabels = true;
            dropdown.visibleItemsCount = Math.Max(1, Math.Min(10, values.Count));
            dropdown.itemHeight = Math.Max(38f, dropdown.itemHeight);
            dropdown.ReloadList();
            dropdown.Setup();
            ApplyFontSizes();

            SetValue(values.Contains(previous) ? previous : values[0]);
        }

        public void SetValue(string value)
        {
            if (string.IsNullOrEmpty(value) || dropdown.items == null || dropdown.items.Count == 0) return;
            TweakableDropdownItem item = dropdown.items.FirstOrDefault(x => x != null && x.value == value);
            if (item == null) item = dropdown.items.FirstOrDefault(x => x != null);
            if (item != null) dropdown.SelectItem(item);
            ApplyFontSizes();
        }

        private void NeutralizeSpecializedTemplate()
        {
            // HitSound/Ease専用行を使わず、通常のエディタ用ドロップダウンとして使う。
            dropdown.enumTypeString = string.Empty;
            dropdown.localizeEnumStrings = false;
            dropdown.useCustomLabels = false;
            dropdown.suggestiveList = false;
            dropdown.rememberLastSearchText = false;
            dropdown.rememberLastScrollPosition = false;
            dropdown.selectedPrefab = dropdown.dropdownItemPrefab;
        }

        private void ApplyFontSizes()
        {
            if (dropdown.inputField != null)
            {
                if (dropdown.inputField.textComponent != null)
                    dropdown.inputField.textComponent.fontSize = Math.Max(17f, dropdown.inputField.textComponent.fontSize);
                TMP_Text placeholder = dropdown.inputField.placeholder as TMP_Text;
                if (placeholder != null) placeholder.fontSize = Math.Max(17f, placeholder.fontSize);
            }

            if (dropdown.items == null) return;
            foreach (TweakableDropdownItem item in dropdown.items)
                if (item != null && item.text != null)
                    item.text.fontSize = Math.Max(16f, item.text.fontSize);
        }
    }
}
