using System;
using System.Collections.Generic;
using StoryAPI.Court.Json;

namespace StoryAPI.Court.Content
{
    /// <summary>
    /// Текст на нескольких языках.
    ///
    /// В файле это либо строка (тогда она одинакова везде), либо объект с одним
    /// ключом на язык. Язык выбирается тот, на котором сейчас игра, но если его нет,
    /// берётся язык по умолчанию, а потом первый попавшийся.
    /// </summary>
    public class LocalizedText
    {
        private readonly Dictionary<string, string> _byLanguage = new Dictionary<string, string>();
        private string _plain;

        public LocalizedText()
        {
        }

        public LocalizedText(string plain)
        {
            _plain = plain;
        }

        public bool IsEmpty
        {
            get { return string.IsNullOrEmpty(_plain) && _byLanguage.Count == 0; }
        }

        public IEnumerable<string> Languages
        {
            get { return _byLanguage.Keys; }
        }

        public void SetPlain(string value)
        {
            _plain = value;
        }

        public void SetLanguage(string language, string value)
        {
            if (string.IsNullOrEmpty(language)) return;
            _byLanguage[language] = value;
        }

        public string Get(string language, string fallbackLanguage)
        {
            string value;
            if (!string.IsNullOrEmpty(language) && _byLanguage.TryGetValue(language, out value) && !string.IsNullOrEmpty(value))
            {
                return value;
            }
            if (!string.IsNullOrEmpty(fallbackLanguage) && _byLanguage.TryGetValue(fallbackLanguage, out value) && !string.IsNullOrEmpty(value))
            {
                return value;
            }
            foreach (var pair in _byLanguage)
            {
                if (!string.IsNullOrEmpty(pair.Value)) return pair.Value;
            }
            return _plain ?? string.Empty;
        }
    }

    /// <summary>
    /// Ошибка в файле контента: где и что не так.
    /// </summary>
    public class ContentError
    {
        public string File { get; private set; }
        public string Field { get; private set; }
        public string Message { get; private set; }

        public ContentError(string file, string field, string message)
        {
            File = file;
            Field = field;
            Message = message;
        }

        public override string ToString()
        {
            return File + ": поле '" + Field + "': " + Message;
        }
    }

    /// <summary>
    /// Персонаж дела.
    /// </summary>
    public class CharacterDefinition
    {
        public string Id;
        public LocalizedText Name = new LocalizedText();
        public LocalizedText Title = new LocalizedText();
        public LocalizedText Profile = new LocalizedText();
        public string Color = "FFFFFF";
        public bool Protagonist;
        public readonly Dictionary<string, string> Poses = new Dictionary<string, string>();
    }

    /// <summary>
    /// Улика.
    /// </summary>
    public class EvidenceDefinition
    {
        public string Id;
        public LocalizedText Name = new LocalizedText();
        public LocalizedText Description = new LocalizedText();
        public LocalizedText DetailedDescription = new LocalizedText();
        public string ImageKey = string.Empty;
        public LocalizedText Category = new LocalizedText();
        public LocalizedText DiscoveryLocation = new LocalizedText();
        public bool Hidden;
        public bool TruthBulletEnabled = true;
    }

    /// <summary>
    /// Точка осмотра в локации.
    /// </summary>
    public class HotspotDefinition
    {
        public string Id;
        public float X, Y, Width, Height;
        public LocalizedText Text = new LocalizedText();
        public readonly List<string> Grants = new List<string>();
        public readonly List<string> SetsFlags = new List<string>();
        public readonly List<string> RequiresFlags = new List<string>();
        public readonly List<string> Requires = new List<string>();
        public bool Once = true;
    }

    /// <summary>
    /// Диалог в локации.
    /// </summary>
    public class DialogueLine
    {
        public string Speaker;
        public string Expression;
        public LocalizedText Text = new LocalizedText();
        public bool Emphasised;
    }

    public class DialogueDefinition
    {
        public string Id;
        public string Character;
        public readonly List<DialogueLine> Lines = new List<DialogueLine>();
        public readonly List<string> Grants = new List<string>();
        public bool Once = true;
    }

    /// <summary>
    /// Локация расследования.
    /// </summary>
    public class LocationDefinition
    {
        public string Id;
        public LocalizedText Name = new LocalizedText();
        public string Background = string.Empty;
        public string Music = string.Empty;
        public LocalizedText EmptyExamine = new LocalizedText();
        public readonly List<string> Exits = new List<string>();
        public readonly List<string> RequiresFlags = new List<string>();
        public readonly List<HotspotDefinition> Hotspots = new List<HotspotDefinition>();
        public readonly List<DialogueDefinition> Dialogues = new List<DialogueDefinition>();
    }

    /// <summary>
    /// Реплика в суде.
    /// </summary>
    public class TrialLine
    {
        public string Speaker;
        public string Expression;
        public LocalizedText Text = new LocalizedText();
        public bool Emphasised;
    }

    /// <summary>
    /// Этап суда. Тип определяет, что за этап: реплики, мини-игра или что-то ещё.
    /// </summary>
    public class TrialStage
    {
        public string Id;
        public string Type = "dialogue";
        public readonly List<TrialLine> Lines = new List<TrialLine>();
        public string MinigameType;
        public string MinigameId;
        public readonly List<string> AllowedEvidence = new List<string>();
        public string OnComplete;
        public string OnFail;
        public readonly Dictionary<string, JsonValue> Minigame = new Dictionary<string, JsonValue>();
    }

    /// <summary>
    /// Классный суд.
    /// </summary>
    public class TrialDefinition
    {
        public string Id;
        public LocalizedText Title = new LocalizedText();
        public string Background = string.Empty;
        public string Music = string.Empty;
        public float Health = 1000f;
        public float Penalty = 200f;
        public float DestroyPenalty = 300f;
        public float DeathThreshold;
        public readonly List<string> Participants = new List<string>();
        public string ReturnLabel;
        public readonly List<TrialStage> Stages = new List<TrialStage>();
        public readonly Dictionary<string, string> Verdicts = new Dictionary<string, string>();
    }

    /// <summary>
    /// Оформление дела.
    /// </summary>
    public class ThemeDefinition
    {
        public float TextSize = 30f;
        public string AccentColor = "FF3B6B";
        public string WindowColor = "0B0B12";
        public string NamePlateColor = "FFD24A";
        public string InvestigateMusic = string.Empty;
        public string TrialMusic = string.Empty;
    }

    /// <summary>
    /// Дело целиком: персонажи, улики, локации, суды.
    /// </summary>
    public class CaseDefinition
    {
        public string Id;
        public LocalizedText Title = new LocalizedText();
        public LocalizedText Subtitle = new LocalizedText();
        public LocalizedText Blurb = new LocalizedText();
        public readonly List<string> Languages = new List<string>();
        public string DefaultLanguage = "en";
        public string PlayerCharacter;
        public string StartLabel;
        public ThemeDefinition Theme = new ThemeDefinition();
        public readonly Dictionary<string, CharacterDefinition> Characters = new Dictionary<string, CharacterDefinition>();
        public readonly Dictionary<string, EvidenceDefinition> Evidence = new Dictionary<string, EvidenceDefinition>();
        public readonly Dictionary<string, LocationDefinition> Locations = new Dictionary<string, LocationDefinition>();
        public readonly Dictionary<string, TrialDefinition> Trials = new Dictionary<string, TrialDefinition>();
    }
}
