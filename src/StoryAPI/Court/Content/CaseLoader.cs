using System;
using System.Collections.Generic;
using System.IO;
using StoryAPI.Court.Json;

namespace StoryAPI.Court.Content
{
    /// <summary>
    /// Читает дело из файла и проверяет, что все ссылки на месте.
    ///
    /// Проверка нужна автору, а не движку: движок должен падать на понятном месте,
    /// а автору нужно увидеть, в каком поле ошибка. Поэтому каждая проблема
    /// сопровождается именем файла и путём до поля.
    /// </summary>
    public class CaseLoader
    {
        private readonly List<ContentError> _errors = new List<ContentError>();
        private string _file = string.Empty;

        public IList<ContentError> Errors
        {
            get { return _errors; }
        }

        public bool HasErrors
        {
            get { return _errors.Count > 0; }
        }

        /// <summary>
        /// Прочитать дело из файла. Возвращает null, если файл не разобран или
        /// в нём есть ошибки; список ошибок остаётся в <see cref="Errors"/>.
        /// </summary>
        public CaseDefinition Load(string path)
        {
            _errors.Clear();
            _file = Path.GetFileName(path);

            if (!File.Exists(path))
            {
                _errors.Add(new ContentError(_file, "файл", "не найден: " + path));
                return null;
            }

            string text;
            try
            {
                text = File.ReadAllText(path);
            }
            catch (Exception e)
            {
                _errors.Add(new ContentError(_file, "файл", "не удалось прочитать: " + e.Message));
                return null;
            }

            JsonValue root;
            try
            {
                root = JsonValue.Parse(text);
            }
            catch (JsonException e)
            {
                _errors.Add(new ContentError(_file, "разбор", e.Message));
                return null;
            }

            if (root.Kind != JsonKind.Object)
            {
                _errors.Add(new ContentError(_file, "корень", "ожидался объект, а получено " + Describe(root)));
                return null;
            }

            var result = new CaseDefinition();
            result.Id = ReadString(root, "id", true);
            ReadLocalized(root, "title", result.Title);
            ReadLocalized(root, "subtitle", result.Subtitle);
            ReadLocalized(root, "blurb", result.Blurb);
            result.Languages.AddRange(root.Get("languages").AsStringList());
            result.DefaultLanguage = ReadString(root, "defaultLanguage", false) ?? "en";
            result.PlayerCharacter = ReadString(root, "playerCharacter", false);
            result.StartLabel = ReadString(root, "startLabel", false);

            ReadTheme(root.Get("theme"), result.Theme);
            ReadCharacters(root.Get("characters"), result.Characters);
            ReadEvidence(root.Get("evidence"), result.Evidence);
            ReadLocations(root.Get("locations"), result.Locations);
            ReadTrials(root.Get("trials"), result.Trials);

            Validate(result);
            return _errors.Count > 0 ? null : result;
        }

        // ------------------------------------------------------------- чтение

        private void ReadTheme(JsonValue node, ThemeDefinition theme)
        {
            if (node.Kind != JsonKind.Object) return;
            if (node.Has("textSize")) theme.TextSize = node.Get("textSize").AsFloat;
            if (node.Has("accentColor")) theme.AccentColor = node.Get("accentColor").AsString;
            if (node.Has("windowColor")) theme.WindowColor = node.Get("windowColor").AsString;
            if (node.Has("namePlateColor")) theme.NamePlateColor = node.Get("namePlateColor").AsString;
            if (node.Has("investigateMusic")) theme.InvestigateMusic = node.Get("investigateMusic").AsString;
            if (node.Has("trialMusic")) theme.TrialMusic = node.Get("trialMusic").AsString;
        }

        private void ReadCharacters(JsonValue node, Dictionary<string, CharacterDefinition> target)
        {
            if (node.Kind != JsonKind.Object) return;
            foreach (var pair in node.AsObject)
            {
                JsonValue body = pair.Value;
                if (body.Kind != JsonKind.Object)
                {
                    _errors.Add(new ContentError(_file, "characters." + pair.Key, "ожидался объект"));
                    continue;
                }
                var character = new CharacterDefinition();
                character.Id = pair.Key;
                ReadLocalized(body, "name", character.Name);
                ReadLocalized(body, "title", character.Title);
                ReadLocalized(body, "profile", character.Profile);
                if (body.Has("color")) character.Color = body.Get("color").AsString;
                if (body.Has("protagonist")) character.Protagonist = body.Get("protagonist").AsBool;
                JsonValue poses = body.Get("poses");
                if (poses.Kind == JsonKind.Object)
                {
                    foreach (var pose in poses.AsObject)
                    {
                        character.Poses[pose.Key] = pose.Value.AsString;
                    }
                }
                target[pair.Key] = character;
            }
        }

        private void ReadEvidence(JsonValue node, Dictionary<string, EvidenceDefinition> target)
        {
            if (node.Kind != JsonKind.Object) return;
            foreach (var pair in node.AsObject)
            {
                JsonValue body = pair.Value;
                if (body.Kind != JsonKind.Object)
                {
                    _errors.Add(new ContentError(_file, "evidence." + pair.Key, "ожидался объект"));
                    continue;
                }
                var evidence = new EvidenceDefinition();
                evidence.Id = pair.Key;
                ReadLocalized(body, "name", evidence.Name);
                ReadLocalized(body, "description", evidence.Description);
                ReadLocalized(body, "detailedDescription", evidence.DetailedDescription);
                if (body.Has("imageKey")) evidence.ImageKey = body.Get("imageKey").AsString;
                ReadLocalized(body, "category", evidence.Category);
                ReadLocalized(body, "discoveryLocation", evidence.DiscoveryLocation);
                if (body.Has("hidden")) evidence.Hidden = body.Get("hidden").AsBool;
                if (body.Has("truthBulletEnabled")) evidence.TruthBulletEnabled = body.Get("truthBulletEnabled").AsBool;
                target[pair.Key] = evidence;
            }
        }

        private void ReadLocations(JsonValue node, Dictionary<string, LocationDefinition> target)
        {
            if (node.Kind != JsonKind.Object) return;
            foreach (var pair in node.AsObject)
            {
                JsonValue body = pair.Value;
                if (body.Kind != JsonKind.Object)
                {
                    _errors.Add(new ContentError(_file, "locations." + pair.Key, "ожидался объект"));
                    continue;
                }
                var location = new LocationDefinition();
                location.Id = pair.Key;
                ReadLocalized(body, "name", location.Name);
                if (body.Has("background")) location.Background = body.Get("background").AsString;
                if (body.Has("music")) location.Music = body.Get("music").AsString;
                ReadLocalized(body, "emptyExamine", location.EmptyExamine);
                location.Exits.AddRange(body.Get("exits").AsStringList());
                location.RequiresFlags.AddRange(body.Get("requiresFlags").AsStringList());

                JsonValue hotspots = body.Get("hotspots");
                if (hotspots.Kind == JsonKind.Array)
                {
                    for (int i = 0; i < hotspots.AsArray.Count; i++)
                    {
                        JsonValue hot = hotspots.Get(i);
                        string path = "locations." + pair.Key + ".hotspots[" + i + "]";
                        if (hot.Kind != JsonKind.Object)
                        {
                            _errors.Add(new ContentError(_file, path, "ожидался объект"));
                            continue;
                        }
                        var definition = new HotspotDefinition();
                        definition.Id = ReadString(hot, "id", true);
                        List<float> rect = hot.Get("rect").AsFloatList();
                        if (rect.Count < 4)
                        {
                            _errors.Add(new ContentError(_file, path + ".rect", "ожидались четыре числа: x, y, ширина, высота"));
                        }
                        else
                        {
                            definition.X = rect[0];
                            definition.Y = rect[1];
                            definition.Width = rect[2];
                            definition.Height = rect[3];
                        }
                        ReadLocalized(hot, "text", definition.Text);
                        definition.Grants.AddRange(hot.Get("grants").AsStringList());
                        definition.SetsFlags.AddRange(hot.Get("setsFlags").AsStringList());
                        definition.RequiresFlags.AddRange(hot.Get("requiresFlags").AsStringList());
                        definition.Requires.AddRange(hot.Get("requires").AsStringList());
                        if (hot.Has("once")) definition.Once = hot.Get("once").AsBool;
                        location.Hotspots.Add(definition);
                    }
                }

                JsonValue dialogues = body.Get("dialogues");
                if (dialogues.Kind == JsonKind.Array)
                {
                    for (int i = 0; i < dialogues.AsArray.Count; i++)
                    {
                        JsonValue dialogue = dialogues.Get(i);
                        string path = "locations." + pair.Key + ".dialogues[" + i + "]";
                        if (dialogue.Kind != JsonKind.Object)
                        {
                            _errors.Add(new ContentError(_file, path, "ожидался объект"));
                            continue;
                        }
                        var definition = new DialogueDefinition();
                        definition.Id = ReadString(dialogue, "id", true);
                        definition.Character = ReadString(dialogue, "character", false);
                        definition.Grants.AddRange(dialogue.Get("grants").AsStringList());
                        if (dialogue.Has("once")) definition.Once = dialogue.Get("once").AsBool;
                        JsonValue lines = dialogue.Get("lines");
                        for (int j = 0; j < lines.AsArray.Count; j++)
                        {
                            JsonValue line = lines.Get(j);
                            string linePath = path + ".lines[" + j + "]";
                            if (line.Kind != JsonKind.Object)
                            {
                                _errors.Add(new ContentError(_file, linePath, "ожидался объект"));
                                continue;
                            }
                            var parsed = new DialogueLine();
                            parsed.Speaker = ReadString(line, "speaker", false);
                            if (line.Has("expression")) parsed.Expression = line.Get("expression").AsString;
                            ReadLocalized(line, "text", parsed.Text);
                            if (line.Has("emphasised")) parsed.Emphasised = line.Get("emphasised").AsBool;
                            definition.Lines.Add(parsed);
                        }
                        location.Dialogues.Add(definition);
                    }
                }

                target[pair.Key] = location;
            }
        }

        private void ReadTrials(JsonValue node, Dictionary<string, TrialDefinition> target)
        {
            if (node.Kind != JsonKind.Object) return;
            foreach (var pair in node.AsObject)
            {
                JsonValue body = pair.Value;
                if (body.Kind != JsonKind.Object)
                {
                    _errors.Add(new ContentError(_file, "trials." + pair.Key, "ожидался объект"));
                    continue;
                }
                var trial = new TrialDefinition();
                trial.Id = pair.Key;
                ReadLocalized(body, "title", trial.Title);
                if (body.Has("background")) trial.Background = body.Get("background").AsString;
                if (body.Has("music")) trial.Music = body.Get("music").AsString;
                if (body.Has("health")) trial.Health = body.Get("health").AsFloat;
                if (body.Has("penalty")) trial.Penalty = body.Get("penalty").AsFloat;
                if (body.Has("destroyPenalty")) trial.DestroyPenalty = body.Get("destroyPenalty").AsFloat;
                if (body.Has("deathThreshold")) trial.DeathThreshold = body.Get("deathThreshold").AsFloat;
                trial.Participants.AddRange(body.Get("participants").AsStringList());
                if (body.Has("returnLabel")) trial.ReturnLabel = body.Get("returnLabel").AsString;

                JsonValue verdicts = body.Get("verdicts");
                if (verdicts.Kind == JsonKind.Object)
                {
                    foreach (var verdict in verdicts.AsObject)
                    {
                        trial.Verdicts[verdict.Key] = verdict.Value.AsString;
                    }
                }

                JsonValue stages = body.Get("stages");
                for (int i = 0; i < stages.AsArray.Count; i++)
                {
                    JsonValue stage = stages.Get(i);
                    string path = "trials." + pair.Key + ".stages[" + i + "]";
                    if (stage.Kind != JsonKind.Object)
                    {
                        _errors.Add(new ContentError(_file, path, "ожидался объект"));
                        continue;
                    }
                    var parsed = new TrialStage();
                    parsed.Id = ReadString(stage, "id", true);
                    if (stage.Has("type")) parsed.Type = stage.Get("type").AsString;
                    if (stage.Has("minigameType")) parsed.MinigameType = stage.Get("minigameType").AsString;
                    if (stage.Has("minigameId")) parsed.MinigameId = stage.Get("minigameId").AsString;
                    parsed.AllowedEvidence.AddRange(stage.Get("allowedEvidence").AsStringList());
                    if (stage.Has("onComplete")) parsed.OnComplete = stage.Get("onComplete").AsString;
                    if (stage.Has("onFail")) parsed.OnFail = stage.Get("onFail").AsString;
                    JsonValue minigame = stage.Get("minigame");
                    if (minigame.Kind == JsonKind.Object)
                    {
                        foreach (var setting in minigame.AsObject)
                        {
                            parsed.Minigame[setting.Key] = setting.Value;
                        }
                    }
                    JsonValue lines = stage.Get("lines");
                    for (int j = 0; j < lines.AsArray.Count; j++)
                    {
                        JsonValue line = lines.Get(j);
                        string linePath = path + ".lines[" + j + "]";
                        if (line.Kind != JsonKind.Object)
                        {
                            _errors.Add(new ContentError(_file, linePath, "ожидался объект"));
                            continue;
                        }
                        var parsedLine = new TrialLine();
                        parsedLine.Speaker = ReadString(line, "speaker", false);
                        if (line.Has("expression")) parsedLine.Expression = line.Get("expression").AsString;
                        ReadLocalized(line, "text", parsedLine.Text);
                        if (line.Has("emphasised")) parsedLine.Emphasised = line.Get("emphasised").AsBool;
                        parsed.Lines.Add(parsedLine);
                    }
                    trial.Stages.Add(parsed);
                }

                target[pair.Key] = trial;
            }
        }

        // --------------------------------------------------------- проверка

        private void Validate(CaseDefinition content)
        {
            if (string.IsNullOrEmpty(content.Id))
            {
                _errors.Add(new ContentError(_file, "id", "обязательное поле"));
            }

            foreach (var character in content.Characters.Values)
            {
                if (character.Name.IsEmpty)
                {
                    _errors.Add(new ContentError(_file, "characters." + character.Id + ".name", "обязательное поле"));
                }
            }

            if (!string.IsNullOrEmpty(content.PlayerCharacter) && !content.Characters.ContainsKey(content.PlayerCharacter))
            {
                _errors.Add(new ContentError(_file, "playerCharacter", "нет персонажа '" + content.PlayerCharacter + "'"));
            }

            foreach (var evidence in content.Evidence.Values)
            {
                if (evidence.Name.IsEmpty)
                {
                    _errors.Add(new ContentError(_file, "evidence." + evidence.Id + ".name", "обязательное поле"));
                }
            }

            foreach (var location in content.Locations.Values)
            {
                foreach (string exit in location.Exits)
                {
                    if (!content.Locations.ContainsKey(exit))
                    {
                        _errors.Add(new ContentError(_file, "locations." + location.Id + ".exits", "нет локации '" + exit + "'"));
                    }
                }
                foreach (var hotspot in location.Hotspots)
                {
                    string path = "locations." + location.Id + ".hotspots." + hotspot.Id;
                    if (string.IsNullOrEmpty(hotspot.Id))
                    {
                        _errors.Add(new ContentError(_file, path, "обязательное поле 'id'"));
                    }
                    foreach (string granted in hotspot.Grants)
                    {
                        if (!content.Evidence.ContainsKey(granted))
                        {
                            _errors.Add(new ContentError(_file, path + ".grants", "нет улики '" + granted + "'"));
                        }
                    }
                    foreach (string needed in hotspot.Requires)
                    {
                        if (!content.Evidence.ContainsKey(needed))
                        {
                            _errors.Add(new ContentError(_file, path + ".requires", "нет улики '" + needed + "'"));
                        }
                    }
                }
                foreach (var dialogue in location.Dialogues)
                {
                    string path = "locations." + location.Id + ".dialogues." + dialogue.Id;
                    if (!string.IsNullOrEmpty(dialogue.Character) && !content.Characters.ContainsKey(dialogue.Character))
                    {
                        _errors.Add(new ContentError(_file, path + ".character", "нет персонажа '" + dialogue.Character + "'"));
                    }
                    foreach (string granted in dialogue.Grants)
                    {
                        if (!content.Evidence.ContainsKey(granted))
                        {
                            _errors.Add(new ContentError(_file, path + ".grants", "нет улики '" + granted + "'"));
                        }
                    }
                }
            }

            foreach (var trial in content.Trials.Values)
            {
                string path = "trials." + trial.Id;
                foreach (string participant in trial.Participants)
                {
                    if (!content.Characters.ContainsKey(participant))
                    {
                        _errors.Add(new ContentError(_file, path + ".participants", "нет персонажа '" + participant + "'"));
                    }
                }
                foreach (var stage in trial.Stages)
                {
                    string stagePath = path + ".stages." + stage.Id;
                    foreach (string allowed in stage.AllowedEvidence)
                    {
                        if (!content.Evidence.ContainsKey(allowed))
                        {
                            _errors.Add(new ContentError(_file, stagePath + ".allowedEvidence", "нет улики '" + allowed + "'"));
                        }
                    }
                    foreach (var line in stage.Lines)
                    {
                        if (!string.IsNullOrEmpty(line.Speaker) && !content.Characters.ContainsKey(line.Speaker))
                        {
                            _errors.Add(new ContentError(_file, stagePath + ".lines", "нет персонажа '" + line.Speaker + "'"));
                        }
                    }
                }
            }
        }

        // --------------------------------------------------------- вспомогательное

        private string ReadString(JsonValue node, string key, bool required)
        {
            if (!node.Has(key))
            {
                if (required)
                {
                    _errors.Add(new ContentError(_file, key, "обязательное поле"));
                }
                return null;
            }
            return node.Get(key).AsString;
        }

        private void ReadLocalized(JsonValue node, string key, LocalizedText target)
        {
            JsonValue value = node.Get(key);
            switch (value.Kind)
            {
                case JsonKind.String:
                    target.SetPlain(value.AsString);
                    break;
                case JsonKind.Object:
                    foreach (var pair in value.AsObject)
                    {
                        target.SetLanguage(pair.Key, pair.Value.AsString);
                    }
                    break;
                case JsonKind.Null:
                    break;
                default:
                    _errors.Add(new ContentError(_file, key, "ожидалась строка или объект с языками, а получено " + Describe(value)));
                    break;
            }
        }

        private static string Describe(JsonValue value)
        {
            switch (value.Kind)
            {
                case JsonKind.Null: return "ничего";
                case JsonKind.Bool: return "логическое значение";
                case JsonKind.Number: return "число";
                case JsonKind.String: return "строка";
                case JsonKind.Array: return "массив";
                default: return "объект";
            }
        }
    }
}
