using CommunityToolkit.Mvvm.ComponentModel;
using NihongoVocab.Services;

namespace NihongoVocab.Models
{
    public enum CardInteractionState
    {
        Normal = 0,
        PendingRemember = 1,
        PendingForget = 2
    }

    public partial class StudyCardItem : ObservableObject
    {
        public Word Word { get; }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsPendingRemember))]
        [NotifyPropertyChangedFor(nameof(IsPendingForget))]
        private CardInteractionState _state = CardInteractionState.Normal;

        public bool IsPendingRemember => State == CardInteractionState.PendingRemember;
        public bool IsPendingForget => State == CardInteractionState.PendingForget;

        public string ConfirmRememberTip => LocalizationService.Instance.GetString("ConfirmRememberTip", "再次左键确认记得");
        public string ConfirmForgetTip => LocalizationService.Instance.GetString("ConfirmForgetTip", "再次右键确认遗忘");

        public StudyCardItem(Word word)
        {
            Word = word;
        }

        public void ResetState()
        {
            State = CardInteractionState.Normal;
        }
    }
}
