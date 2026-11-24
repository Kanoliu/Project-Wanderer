using Godot;
using System;
using System.Collections.Generic;

public partial class CreatorMenu : ColorRect
{
	private sealed record Entry(string Section, string Label, Action Run);
	private readonly List<Entry> _catalog = new();
	private readonly List<Entry> _matches = new();
	private OptionButton _section;
	private LineEdit _search;
	private ItemList _entries;
	private Button _run;
	private Control _previousFocus;
	private bool _previousPause;
	public event Action<bool> ModalChanged;
	public Func<string> CurrentLocation { get; set; }

	public override void _Ready()
	{
		_section = GetNode<OptionButton>("%Section");
		_search = GetNode<LineEdit>("%Search");
		_entries = GetNode<ItemList>("%Entries");
		_run = GetNode<Button>("%Run");
		_section.AddItem("全部");
		_section.ItemSelected += _ => Refresh();
		_search.TextChanged += _ => Refresh();
		_search.TextSubmitted += _ => Execute();
		_entries.ItemActivated += _ => Execute();
		_run.Pressed += Execute;
		GetNode<Button>("Panel/Stack/Actions/Back").Pressed += Close;
	}

	public void Register(string section, string label, Action action)
	{
		if (!_catalog.Exists(entry => entry.Section == section))
			_section.AddItem(section);
		_catalog.Add(new Entry(section, label.Replace("\r", "").Replace("\n", "　"), action));
	}

	public override void _Input(InputEvent input)
	{
		if (input is not InputEventKey { Pressed: true, Echo: false } key)
			return;
		if (key.Keycode != Key.F10 && !(Visible && key.Keycode == Key.Escape))
			return;
		GetViewport().SetInputAsHandled();
		if (Visible) Close();
		else Open();
	}

	private void Open()
	{
		_previousPause = GetTree().Paused;
		_previousFocus = GetViewport().GuiGetFocusOwner();
		ModalChanged?.Invoke(true);
		GetTree().Paused = true;
		GetNode<Label>("%Location").Text = "当前位置　" + CurrentLocation?.Invoke();
		Show();
		Refresh();
		_search.GrabFocus();
		_search.SelectAll();
	}

	private void Close()
	{
		Hide();
		GetTree().Paused = _previousPause;
		ModalChanged?.Invoke(false);
		if (IsInstanceValid(_previousFocus) && _previousFocus.IsVisibleInTree())
			_previousFocus.GrabFocus();
	}

	private void Refresh()
	{
		_entries.Clear();
		_matches.Clear();
		string section = _section.GetItemText(_section.Selected);
		foreach (Entry entry in _catalog)
		{
			if ((_section.Selected != 0 && entry.Section != section) ||
				!entry.Label.Contains(_search.Text.Trim(), StringComparison.OrdinalIgnoreCase))
				continue;
			_entries.AddItem(entry.Label);
			_matches.Add(entry);
		}
		_run.Disabled = _matches.Count == 0;
		if (_run.Disabled)
		{
			_entries.AddItem("没有匹配项");
			_entries.SetItemDisabled(0, true);
		}
		else _entries.Select(0);
	}

	private void Execute()
	{
		int[] selected = _entries.GetSelectedItems();
		if (selected.Length == 0 || selected[0] >= _matches.Count) return;
		Entry entry = _matches[selected[0]];
		Close();
		entry.Run();
	}
}
