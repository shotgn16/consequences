namespace Consequences.Game;

public enum StepKind { Text, Drawing }

/// <summary>One fold of the paper.</summary>
public class Step
{
    /// <summary>Instruction shown to the writer, e.g. "Write a man's name".</summary>
    public string Prompt { get; set; } = "";
    /// <summary>Connective shown before the answer when the paper is read out, e.g. "met", "at", "He wore".</summary>
    public string Prefix { get; set; } = "";
    /// <summary>Text shown after the answer when read out (e.g. closing quote mark).</summary>
    public string Suffix { get; set; } = "";
    public StepKind Kind { get; set; } = StepKind.Text;
    /// <summary>Players may add a drawing to this text fold (people, outfits, places).</summary>
    public bool Drawable { get; set; }
    /// <summary>What to draw, e.g. "Draw him!"</summary>
    public string DrawHint { get; set; } = "";
    /// <summary>Suggestion category used by "Inspire me", auto-fill and bots.</summary>
    public string Category { get; set; } = "anything";
    public string Placeholder { get; set; } = "";
}

public class Template
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public bool PictureMode { get; set; }
    public List<Step> Steps { get; set; } = new();

    public Template Clone() => new()
    {
        Id = Id, Name = Name, Description = Description, PictureMode = PictureMode,
        Steps = Steps.Select(s => new Step
        {
            Prompt = s.Prompt, Prefix = s.Prefix, Suffix = s.Suffix, Kind = s.Kind, Drawable = s.Drawable,
            DrawHint = s.DrawHint, Category = s.Category, Placeholder = s.Placeholder
        }).ToList()
    };
}

public static class Templates
{
    private static Step T(string prompt, string prefix, string cat, string placeholder, bool drawable = false, string drawHint = "", string suffix = "")
        => new() { Prompt = prompt, Prefix = prefix, Suffix = suffix, Category = cat, Placeholder = placeholder, Drawable = drawable, DrawHint = drawHint };

    private static Step D(string prompt, string hint) => new() { Prompt = prompt, Kind = StepKind.Drawing, DrawHint = hint, Category = "drawing" };

    public static readonly IReadOnlyList<Template> BuiltIn = new List<Template>
    {
        new()
        {
            Id = "classic", Name = "Classic", Description = "The traditional 11-fold game: he met her, where, what they wore, what they said, and the consequence.",
            Steps =
            {
                T("Write an adjective to describe a man", "", "adjective", "e.g. Sweaty"),
                T("Write a man's name: famous, someone you know, or made up", "", "maleName", "e.g. Captain Haddock", true, "Draw him!"),
                T("Write an adjective to describe a woman", "met", "adjective", "e.g. Mysterious"),
                T("Write a woman's name: famous, someone you know, or made up", "", "femaleName", "e.g. The Queen", true, "Draw her!"),
                T("Where did they meet?", "at", "place", "e.g. the bottom of a well", true, "Draw the scene!"),
                T("What was he wearing?", "He wore", "clothing", "e.g. a tutu made of ham", true, "Draw the outfit!"),
                T("What was she wearing?", "She wore", "clothing", "e.g. nothing but a smile", true, "Draw the outfit!"),
                T("What did he say to her?", "He said to her, “", "saidHe", "e.g. Is this the way to Amarillo?", suffix: "”"),
                T("What did she say to him?", "She said to him, “", "saidShe", "e.g. Not now, I'm busy", suffix: "”"),
                T("What was the consequence?", "And the consequence was", "consequence", "e.g. they opened a llama farm", true, "Draw what happened!"),
                T("What did the world say?", "And the world said, “", "world", "e.g. Well, that's Tuesday ruined", suffix: "”"),
            }
        },
        new()
        {
            Id = "modern", Name = "Anyone meets anyone", Description = "The classic game with gender-neutral prompts: any two characters can meet.",
            Steps =
            {
                T("Write an adjective to describe a character", "", "adjective", "e.g. Soggy"),
                T("Name a character: a person, an animal, anything", "", "anyName", "e.g. A haunted toaster", true, "Draw them!"),
                T("Write another adjective", "met", "adjective", "e.g. Overconfident"),
                T("Name another character", "", "anyName", "e.g. Gordon Ramsay", true, "Draw them!"),
                T("Where did they meet?", "at", "place", "e.g. a silent disco in IKEA", true, "Draw the scene!"),
                T("What was the first one wearing?", "The first wore", "clothing", "e.g. a bin bag", true, "Draw the outfit!"),
                T("What was the second one wearing?", "The second wore", "clothing", "e.g. a crown of sausages", true, "Draw the outfit!"),
                T("What did the first say?", "The first said, “", "saidHe", "e.g. I've got a confession…", suffix: "”"),
                T("What did the second reply?", "The second replied, “", "saidShe", "e.g. I know. I've always known.", suffix: "”"),
                T("What was the consequence?", "And the consequence was", "consequence", "e.g. a 3-part Netflix documentary", true, "Draw what happened!"),
                T("What did the world say?", "And the world said, “", "world", "e.g. Typical.", suffix: "”"),
            }
        },
        new()
        {
            Id = "quick", Name = "Quickie", Description = "Seven folds for a fast round. Good for small groups or short attention spans.",
            Steps =
            {
                T("Name a character", "", "anyName", "e.g. Shrek's accountant", true, "Draw them!"),
                T("Name another character", "met", "anyName", "e.g. Your nan", true, "Draw them!"),
                T("Where did they meet?", "at", "place", "e.g. a volcano", true, "Draw the scene!"),
                T("What did the first say?", "The first said, “", "saidHe", "e.g. You again!", suffix: "”"),
                T("What did the second reply?", "The second replied, “", "saidShe", "e.g. Who are you?", suffix: "”"),
                T("What was the consequence?", "And the consequence was", "consequence", "e.g. a national holiday", true, "Draw what happened!"),
                T("What did the world say?", "And the world said, “", "world", "e.g. About time.", suffix: "”"),
            }
        },
        new()
        {
            Id = "picture", Name = "Picture Consequences", Description = "Draw a creature in sections: head, body, legs, feet. Lines carry over the fold. Then name it!",
            PictureMode = true,
            Steps =
            {
                D("Draw a HEAD (and neck). Let the neck lines run off the bottom edge.", "Head & neck"),
                D("Draw a BODY with arms. Start from the lines poking over the fold at the top.", "Body & arms"),
                D("Draw the LEGS. Carry on from the lines at the top.", "Legs"),
                D("Draw the FEET or shoes, plus some ground to stand on.", "Feet & ground"),
                new() { Prompt = "Give this creature a name (you can't see it!)", Prefix = "Behold:", Category = "creatureName", Placeholder = "e.g. Sir Kevin the Unwashed" },
            }
        },
        new()
        {
            Id = "scenes", Name = "Scene Painter", Description = "Every fold is a location to paint: build a journey nobody planned.",
            Steps =
            {
                T("Name a hero", "Once upon a time,", "anyName", "e.g. a nervous goose", true, "Draw the hero!"),
                T("Where did they start their journey?", "set off from", "place", "e.g. a car park in Slough", true, "Paint the place!"),
                T("Where did they go next?", "travelled to", "place", "e.g. the moon's gift shop", true, "Paint the place!"),
                T("Who did they meet there?", "where they met", "anyName", "e.g. a tired wizard", true, "Draw them!"),
                T("Where did they end up?", "and finally arrived at", "place", "e.g. a hot tub in Narnia", true, "Paint the place!"),
                T("What was the consequence?", "And the consequence was", "consequence", "e.g. nobody was invited back", true, "Draw what happened!"),
            }
        },
    };

    public static Template? Get(string id) => BuiltIn.FirstOrDefault(t => t.Id == id)?.Clone();
}
