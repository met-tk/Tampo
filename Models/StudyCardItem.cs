using CommunityToolkit.Mvvm.ComponentModel;
using NihongoVocab.Services;

namespace NihongoVocab.Models
{
    public enum CardInteractionState
    {
        Normal = 0,
        PendingRemember = 1,
        PendingForget = 2,
        Exiting = 3
    }

    public partial class StudyCardItem : ObservableObject
    {
        public Word Word { get; }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsPendingRemember))]
        [NotifyPropertyChangedFor(nameof(IsPendingForget))]
        [NotifyPropertyChangedFor(nameof(IsNormal))]
        private CardInteractionState _state = CardInteractionState.Normal;

        [ObservableProperty]
        private double _cardOpacity = 1.0;

        [ObservableProperty]
        private double _cardScale = 1.0;

        public bool IsPendingRemember => State == CardInteractionState.PendingRemember;
        public bool IsPendingForget => State == CardInteractionState.PendingForget;
        public bool IsNormal => State == CardInteractionState.Normal;

        public string ConfirmRememberTip => LocalizationService.Instance.GetString("ConfirmRememberTip", "再次左键确认记得");
        public string ConfirmForgetTip => LocalizationService.Instance.GetString("ConfirmForgetTip", "再次右键确认遗忘");

        public StudyCardItem(Word word)
        {
            Word = word;
            LocalizationService.Instance.LanguageChanged += (s, e) => OnPropertyChanged(string.Empty);
        }

        public void ResetState()
        {
            State = CardInteractionState.Normal;
            CardOpacity = 1.0;
            CardScale = 1.0;
        }
    }
}
