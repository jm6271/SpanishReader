using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using SpanishReader.Core.Abstractions;
using SpanishReader.Core.Models;
using SpanishReader.Core.ViewModels;

namespace SpanishReader.Views;

public sealed partial class ReadingPage : Page, INavigationAware
{
    private readonly Dictionary<DependencyObject, Hyperlink> _sentenceLinks = [];
    private readonly Dictionary<Hyperlink, TextRange> _sentenceRanges = [];
    private readonly Dictionary<Hyperlink, StorySentence> _sentenceModels = [];
    private Hyperlink? _hoveredHyperlink;
    private Hyperlink? _keyboardHyperlink;
    private Hyperlink? _selectedHyperlink;
    private SentencePointerGesture? _pointerGesture;
    public ReadingPageViewModel ViewModel { get; }

    public ReadingPage(ReadingPageViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        // Hyperlinks are inline text, so pointer events belong to their text host.
        // Listen even when the native text selection/link handling marks them handled.
        StoryContent.AddHandler(PointerMovedEvent, new PointerEventHandler(OnStoryPointerMoved), true);
        StoryContent.AddHandler(PointerPressedEvent, new PointerEventHandler(OnStoryPointerPressed), true);
        StoryContent.AddHandler(PointerReleasedEvent, new PointerEventHandler(OnStoryPointerReleased), true);
        StoryContent.AddHandler(PointerExitedEvent, new PointerEventHandler((_, _) => ClearHover()), true);
        StoryContent.AddHandler(PointerCanceledEvent, new PointerEventHandler((_, _) =>
        {
            _pointerGesture = null;
            ClearHover();
        }), true);
        StoryContent.SizeChanged += (_, _) => ClearHover();
        StoryScroll.ViewChanging += (_, _) =>
        {
            if (_pointerGesture is { } gesture) gesture.IsCanceled = true;
            ClearHover();
        };
        Unloaded += (_, _) =>
        {
            _hoveredHyperlink = _keyboardHyperlink = null;
            _pointerGesture = null;
            UpdateSentenceHighlight();
            ViewModel.Deactivate();
        };
    }

    public void OnNavigatedTo(object? parameter)
    {
        if (parameter is not Story story)
            throw new ArgumentException("A story is required to open the reader.", nameof(parameter));
        ViewModel.PrepareForDisplay(story);
        _hoveredHyperlink = _keyboardHyperlink = null;
        _selectedHyperlink = null;
        SentenceHighlight.Ranges.Clear();
        _sentenceLinks.Clear();
        _sentenceRanges.Clear();
        _sentenceModels.Clear();
        _pointerGesture = null;
        StoryContent.Blocks.Clear();
        var paragraph = new Paragraph();
        var textIndex = 0;
        foreach (var sentence in ViewModel.Sentences)
        {
            var hyperlink = new Hyperlink { UnderlineStyle = UnderlineStyle.None };
            BindDocumentInk(hyperlink);
            AutomationProperties.SetAutomationId(hyperlink, $"Sentence{sentence.Id}");
            AutomationProperties.SetName(hyperlink, sentence.SpanishText.Trim());
            // Keep embedded whitespace in a Run: Hyperlink rejects LineBreak
            // children at runtime, even though InlineCollection accepts the type.
            // Keep paragraph breaks and separating whitespace outside the link:
            // otherwise WinUI also treats that empty space as hyperlink content.
            var text = sentence.SpanishText;
            var leadingWhitespace = text.Length - text.TrimStart().Length;
            var contentEnd = text.TrimEnd().Length;
            if (leadingWhitespace > 0) paragraph.Inlines.Add(new Run { Text = text[..leadingWhitespace] });
            var run = new Run { Text = text[leadingWhitespace..contentEnd] };
            // Native hyperlink hover changes the link's foreground even outside
            // the strict hit area. A local foreground on its Run overrides that
            // inherited color, leaving the sentence highlighter in charge.
            BindDocumentInk(run);
            hyperlink.Inlines.Add(run);
            _sentenceLinks.Add(run, hyperlink);
            _sentenceLinks.Add(hyperlink, hyperlink);
            _sentenceModels.Add(hyperlink, sentence);
            // TextHighlighter uses plain-text character indices, not TextPointer
            // offsets (which also count inline element boundaries). Exclude the
            // inter-sentence whitespace from the visible highlight.
            _sentenceRanges.Add(hyperlink, new TextRange
            {
                StartIndex = textIndex + leadingWhitespace,
                Length = sentence.SpanishText.Trim().Length
            });
            textIndex += sentence.SpanishText.Length;
            hyperlink.Click += async (link, _) =>
            {
                // Pointer activation is handled on release using exact text hit
                // testing. Keep native Click for keyboard and UI Automation.
                if (_pointerGesture is null) await ShowSentenceAsync(link);
            };
            // Keep native Tab/Enter navigation and a visible focus cue without
            // reintroducing underlines when a sentence is focused or selected.
            hyperlink.GotFocus += (_, _) =>
            {
                _keyboardHyperlink = hyperlink.FocusState == FocusState.Keyboard ? hyperlink : null;
                UpdateSentenceHighlight();
            };
            hyperlink.LostFocus += (_, _) =>
            {
                if (ReferenceEquals(_keyboardHyperlink, hyperlink)) _keyboardHyperlink = null;
                UpdateSentenceHighlight();
            };
            paragraph.Inlines.Add(hyperlink);
            if (contentEnd < text.Length) paragraph.Inlines.Add(new Run { Text = text[contentEnd..] });
        }
        StoryContent.Blocks.Add(paragraph);
        // A cached page can still have its previous extent before the next layout.
        // Also reset on Loaded, when the new document's scroll extent is known.
        StoryScroll.ChangeView(null, 0, null, true);
        TranslationScroll.ChangeView(null, 0, null, true);
        Loaded -= ResetScrollOnLoaded;
        Loaded += ResetScrollOnLoaded;
    }

    private void BindDocumentInk(TextElement element) =>
        BindingOperations.SetBinding(element, TextElement.ForegroundProperty, new Binding
        {
            Source = StoryContent,
            Path = new PropertyPath(nameof(StoryContent.Foreground)),
            Mode = BindingMode.OneWay
        });

    private async Task ShowSentenceAsync(Hyperlink hyperlink)
    {
        if (!_sentenceModels.TryGetValue(hyperlink, out var sentence)) return;
        // Selection outlives hover and focus, including while a translation is
        // loading. Only another activated sentence (or a new story) replaces it.
        _selectedHyperlink = hyperlink;
        UpdateSentenceHighlight();
        TranslationScroll.ChangeView(null, 0, null, true);
        await ViewModel.SelectSentenceCommand.ExecuteAsync(sentence);
    }

    private void OnStoryPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(StoryContent);
        if (!point.Properties.IsLeftButtonPressed && e.Pointer.PointerDeviceType != Microsoft.UI.Input.PointerDeviceType.Touch) return;
        _pointerGesture = new SentencePointerGesture(e.Pointer.PointerId, point.Position, HitTestSentence(point.Position));
    }

    private async void OnStoryPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_pointerGesture is not { } gesture || gesture.PointerId != e.Pointer.PointerId) return;
        var point = e.GetCurrentPoint(StoryContent).Position;
        gesture.UpdatePosition(point);
        // Native Click can run before or after this routed handler. Keep it
        // suppressed until this input dispatch finishes, so activation happens
        // once and always uses the actual release position, including touch.
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            if (ReferenceEquals(_pointerGesture, gesture)) _pointerGesture = null;
        });
        if (!gesture.IsCanceled && gesture.PressedLink is { } link &&
            ReferenceEquals(link, HitTestSentence(point)))
        {
            await ShowSentenceAsync(link);
        }
    }

    private void OnStoryPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(StoryContent).Position;
        if (_pointerGesture is { } gesture && gesture.PointerId == e.Pointer.PointerId) gesture.UpdatePosition(point);
        var hovered = HitTestSentence(point);
        if (ReferenceEquals(_hoveredHyperlink, hovered)) return;
        _hoveredHyperlink = hovered;
        UpdateSentenceHighlight();
    }

    private Hyperlink? HitTestSentence(Windows.Foundation.Point point)
    {
        var position = StoryContent.GetPositionFromPoint(point);
        Hyperlink? hovered = null;
        // Hit testing can return the nearest insertion position in a blank
        // margin. Check its neighboring carets so empty paper stays unmarked.
        if (position is not null &&
            IsPointOverCharacter(position, point))
        {
            _sentenceLinks.TryGetValue(position.Parent, out hovered);
        }
        return hovered;
    }

    private static bool IsPointOverCharacter(TextPointer position, Windows.Foundation.Point point)
    {
        // WinUI returns zero-width caret rectangles, even from GetCharacterRect.
        // Two adjacent carets on the same line enclose the actual character.
        // Do not join carets across a wrap or paragraph break.
        for (var step = -1; step <= 1; step += 2)
        {
            var direction = step < 0 ? LogicalDirection.Backward : LogicalDirection.Forward;
            var neighbor = position.GetPositionAtOffset(step, direction);
            if (neighbor is null) continue;
            var caret = position.GetCharacterRect(direction);
            var adjacentCaret = neighbor.GetCharacterRect(direction);
            if (Math.Abs(caret.Y - adjacentCaret.Y) > 0.1) continue;
            var left = Math.Min(caret.X, adjacentCaret.X);
            var right = Math.Max(caret.X, adjacentCaret.X);
            if (point.X >= left && point.X <= right &&
                point.Y >= caret.Top && point.Y < caret.Bottom) return true;
        }
        return false;
    }

    private void ClearHover()
    {
        if (_hoveredHyperlink is null) return;
        _hoveredHyperlink = null;
        UpdateSentenceHighlight();
    }

    private void UpdateSentenceHighlight()
    {
        SentenceHighlight.Ranges.Clear();
        if (_selectedHyperlink is { } selected && _sentenceRanges.TryGetValue(selected, out var selectedRange))
        {
            SentenceHighlight.Ranges.Add(selectedRange);
        }
        // Hovering another sentence previews it without clearing the selection.
        if ((_hoveredHyperlink ?? _keyboardHyperlink) is { } hyperlink &&
            !ReferenceEquals(hyperlink, _selectedHyperlink) &&
            _sentenceRanges.TryGetValue(hyperlink, out var range))
        {
            SentenceHighlight.Ranges.Add(range);
        }
    }

    private void ResetScrollOnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= ResetScrollOnLoaded;
        StoryScroll.ChangeView(null, 0, null, true);
        TranslationScroll.ChangeView(null, 0, null, true);
    }

    private void OnReaderSizeChanged(object sender, SizeChangedEventArgs e) =>
        VisualStateManager.GoToState(this, ActualWidth < 900 ? "Narrow" : "Wide", false);

    private sealed class SentencePointerGesture(uint pointerId, Windows.Foundation.Point pressedPosition, Hyperlink? pressedLink)
    {
        public uint PointerId { get; } = pointerId;
        public Hyperlink? PressedLink { get; } = pressedLink;
        public bool IsCanceled { get; set; }

        public void UpdatePosition(Windows.Foundation.Point point)
        {
            // A text selection or a touch scroll is not a sentence click. Use a
            // small DIP threshold so ordinary pointer jitter remains clickable.
            var dx = point.X - pressedPosition.X;
            var dy = point.Y - pressedPosition.Y;
            if (dx * dx + dy * dy > 16) IsCanceled = true;
        }
    }
}
