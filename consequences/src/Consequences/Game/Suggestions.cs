using System.Text.RegularExpressions;

namespace Consequences.Game;

/// <summary>Random ideas for "Inspire me", auto-fill and computer players.</summary>
public static class Suggestions
{
    private static readonly Dictionary<string, string[]> Bank = new()
    {
        ["adjective"] = new[] { "Sweaty", "Glamorous", "Suspicious", "Damp", "Heroic", "Grumpy", "Sparkly", "Wobbly", "Majestic", "Slightly haunted", "Over-caffeinated", "Smug", "Bewildered", "Crusty", "Dazzling", "Sticky", "Tiny", "Enormous", "Fluffy", "Mischievous", "Lonely", "Fabulous", "Very tired", "Unhinged" },
        ["maleName"] = new[] { "Captain Haddock", "Henry VIII", "Postman Pat", "Uncle Derek", "Gandalf the Beige", "Elvis", "Dave from Accounts", "Sir Isaac Newton", "Your dentist", "Mr Blobby", "Napoleon", "Bob the Builder", "Gordon Ramsay", "King Arthur", "Santa" },
        ["femaleName"] = new[] { "Queen Victoria", "Cleopatra", "Auntie Pam", "Mary Poppins", "Joan of Arc", "Mrs Doubtfire", "Marie Curie", "Your old geography teacher", "Florence Nightingale", "Wonder Nan", "Boudicca", "Miss Trunchbull", "The Tooth Fairy" },
        ["anyName"] = new[] { "A haunted toaster", "Shrek's accountant", "A goose with a grudge", "Your nan", "Dracula", "A sentient traffic cone", "The Prime Minister", "A disappointed wizard", "Two raccoons in a trench coat", "Elvis", "A very small horse", "The Loch Ness Monster", "Sir David Attenborough", "A forgetful robot" },
        ["place"] = new[] { "the bottom of a well", "a Little Chef in 1987", "the moon", "a silent disco in IKEA", "a bouncy castle", "the queue for the loo", "a haunted Greggs", "a llama farm", "the top of Big Ben", "a submarine full of jelly", "the back of a bus", "a wedding in Vegas", "the Eurovision Song Contest", "a garden centre", "inside a whale", "a car park in Slough", "a volcano" },
        ["clothing"] = new[] { "a tutu made of ham", "a full suit of armour", "flip-flops and a monocle", "a bin bag", "pyjamas covered in tiny ducks", "a crown of sausages", "a wetsuit", "a ballgown made of crisp packets", "nothing but a smile", "a Christmas jumper in July", "a traffic cone hat", "wellies on the wrong feet", "a giant inflatable dinosaur costume" },
        ["saidHe"] = new[] { "Is this the way to Amarillo?", "I've lost my left sock.", "Do you come here often?", "Have you seen my llama?", "I'm not wearing any trousers.", "Fancy a Jaffa Cake?", "I've got a confession…", "You remind me of my mum.", "That's not a hat, that's a cat.", "Where's the nearest Greggs?", "I can explain everything." },
        ["saidShe"] = new[] { "Not now, I'm busy.", "I know. I've always known.", "Get lost.", "Only if you bring the cheese.", "My hovercraft is full of eels.", "Who are you?", "Is that a parsnip in your pocket?", "Same time next week?", "Ask my solicitor.", "I'd rather eat a candle.", "Oh go on then." },
        ["consequence"] = new[] { "they opened a llama farm", "a 3-part Netflix documentary", "they were banned from Wales", "a national holiday", "they joined the circus", "the Queen sent a strongly-worded letter", "they got matching tattoos", "seventeen children", "a very awkward Christmas", "they started a cult", "the moon fell off", "they both took up competitive cheese rolling" },
        ["world"] = new[] { "Well, that's Tuesday ruined.", "Typical.", "About time.", "We saw that coming.", "Disgusting! Do it again!", "Not again…", "Pass the popcorn.", "Who ordered the pizza?", "Wahey!", "Hold my tea.", "Nobody tell Mum." },
        ["creatureName"] = new[] { "Sir Kevin the Unwashed", "Gary", "The Great Wobblini", "Professor Snoot", "Brenda", "Lord Flapington", "Captain Kneecaps", "Nigel the Magnificent", "Squiggles", "Her Majesty's Moose" },
        ["anything"] = new[] { "a banana", "an awkward silence", "a surprise party", "Wednesday", "the vicar", "a kazoo solo" },
    };

    public static string Random(string category)
    {
        var list = Bank.TryGetValue(category, out var l) ? l : Bank["anything"];
        return list[System.Random.Shared.Next(list.Length)];
    }
}

/// <summary>Very small "family friendly" filter. Admins can add their own words.</summary>
public static class WordFilter
{
    private static readonly string[] BaseWords =
    {
        "fuck", "fucking", "fucker", "shit", "shitty", "bullshit", "cunt", "bitch", "bastard", "dick", "dickhead", "cock",
        "wanker", "wank", "twat", "prick", "piss", "pissed", "arse", "arsehole", "asshole", "slut", "whore", "bollocks", "tits", "knob", "bellend"
    };

    public static string Clean(string text, IEnumerable<string> extra)
    {
        var words = BaseWords.Concat(extra).Where(w => w.Length > 0).Select(Regex.Escape).ToArray();
        if (words.Length == 0) return text;
        var rx = new Regex(@"\b(" + string.Join("|", words) + @")\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        try { return rx.Replace(text, m => m.Value[0] + new string('*', Math.Max(1, m.Value.Length - 1))); }
        catch (RegexMatchTimeoutException) { return text; }
    }
}
