using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Lotd.FileFormats;

namespace Lotd
{
    static class YgoLeToJson
    {
        static string GetSeriesName(DuelSeries series)
        {
            string seriesName = series.ToString();
            if (series == DuelSeries.None)
            {
                seriesName = "Extra";
            }
            return seriesName;
        }

        public static void Run()
        {
            string outputDir = "LeData";
            string decksDir = Path.Combine(outputDir, "Decks");
            string decksExDir = Path.Combine(outputDir, "DecksEx");
            string duelsDir = Path.Combine(outputDir, "Duels");
            Directory.CreateDirectory(outputDir);
            Directory.CreateDirectory(decksDir);
            Directory.CreateDirectory(decksExDir);
            Directory.CreateDirectory(duelsDir);
            foreach (DuelSeries series in Enum.GetValues(typeof(DuelSeries)))
            {
                Directory.CreateDirectory(Path.Combine(decksDir, GetSeriesName(series)));
                Directory.CreateDirectory(Path.Combine(decksExDir, GetSeriesName(series)));
                Directory.CreateDirectory(Path.Combine(duelsDir, GetSeriesName(series)));
            }

            Dictionary<string, ZibFile> deckFiles = new Dictionary<string, ZibFile>();
            foreach (KeyValuePair<string, ZibFile> file in Program.Manager.Archive.Root.FindFile("decks.zib").LoadData<ZibData>().Files)
            {
                deckFiles[file.Key.ToLowerInvariant()] = file.Value;
            }

            Dictionary<int, DeckData.Item> decksById = new Dictionary<int, DeckData.Item>();

            HashSet<int> deckIdsUsedInDuels = new HashSet<int>();
            foreach (DuelData.Item duel in Program.Manager.DuelData.Items.Values)
            {
                deckIdsUsedInDuels.Add(duel.OpponentDeckId);
                deckIdsUsedInDuels.Add(duel.PlayerDeckId);
            }

            Dictionary<int, CharData.Item> charsById = new Dictionary<int, CharData.Item>();
            foreach (CharData.Item character in Program.Manager.CharData.Items.Values)
            {
                charsById[character.Id] = character;
            }

            LotdDirectory archiveDecksDir = Program.Manager.Archive.Root.FindDirectory("decks");

            HashSet<string> usedDecks = new HashSet<string>();
            Dictionary<string, DuelSeries> seenDecks = new Dictionary<string, DuelSeries>();
            foreach (DeckData.Item deck in Program.Manager.DeckData.Items.Values)
            {
                Dictionary<string, object> deckData = new Dictionary<string, object>();
                deckData["name"] = deck.DeckName.English;
                deckData["signatureCard"] = deck.SignatureCardId;

                string fileName = (deck.DeckFileName.English + ".ydc").ToLowerInvariant();
                ZibFile ydcFile;
                if (deckFiles.TryGetValue(fileName, out ydcFile))
                {
                    decksById[deck.Id1] = deck;
                    seenDecks[fileName] = deck.Series;
                    YdcToDictionary(ydcFile, deckData);
                }
                else
                {
                    Console.WriteLine("[WARNING] Failed to find file '" + deck.DeckFileName + "'");
                }

                bool isKnownDuel = deckIdsUsedInDuels.Contains(deck.Id1);
                string outputFileName = Path.Combine((isKnownDuel ? decksDir : decksExDir), GetSeriesName(deck.Series), deck.DeckFileName + ".json");
                File.WriteAllText(outputFileName, MiniJSON.Json.Serialize(deckData));
            }

            foreach (KeyValuePair<string, ZibFile> file in deckFiles)
            {
                if (!seenDecks.ContainsKey(file.Key))
                {
                    string name = file.Key.Substring(0, file.Key.Length - 4);// Remove the .ydc
                    Dictionary<string, object> deckData = new Dictionary<string, object>();
                    deckData["name"] = name;
                    YdcToDictionary(file.Value, deckData);
                    string outputFileName = Path.Combine(decksExDir, GetSeriesName(DuelSeries.None), name + ".json");
                    File.WriteAllText(outputFileName, MiniJSON.Json.Serialize(deckData));
                }
            }

            // NOTE: Some fixes required to Lotd code to use this:
            // - Change LotdFile.CanLoadArchive to return true for DuelDataBin
            // - In DuelData.cs add an additional reader.ReadInt32() after reading dlcId
            foreach (DuelData.Item duel in Program.Manager.DuelData.Items.Values)
            {
                if (!decksById.ContainsKey(duel.OpponentDeckId) || !decksById.ContainsKey(duel.PlayerDeckId))
                {
                    Console.WriteLine("Failed to find deck ids for duel " + duel.Name.English);
                    continue;
                }

                DeckData.Item deck1 = decksById[duel.OpponentDeckId];
                DeckData.Item deck2 = decksById[duel.PlayerDeckId];

                usedDecks.Add((deck1.DeckFileName.English + ".ydc").ToLowerInvariant());
                usedDecks.Add((deck1.DeckFileName.English + ".ydc").ToLowerInvariant());

                string description = duel.Description.English;
                CharData.Item playerChar, opponentChar;
                if (charsById.TryGetValue(duel.PlayerCharId, out playerChar) && charsById.TryGetValue(duel.OpponentCharId, out opponentChar))
                {
                    if (!string.IsNullOrEmpty(description))
                    {
                        description += "\n\n";
                    }
                    description += playerChar.Name.English + " VS " + opponentChar.Name.English;
                }

                Dictionary<string, object> duelData = new Dictionary<string, object>();
                duelData["name"] = duel.Name.English;
                duelData["description"] = description;
                duelData["displayIndex"] = duel.DisplayIndex;
                duelData["opponentDeck"] = "Decks/" + GetSeriesName(deck1.Series) + "/" + deck1.DeckFileName.English + ".json";
                duelData["playerDeck"] = "Decks/" + GetSeriesName(deck2.Series) + "/" + deck2.DeckFileName.English + ".json";
                string outputFileName = Path.Combine(duelsDir, GetSeriesName(duel.Series), SanitizeFileName(duel.Name.English) + ".json");
                File.WriteAllText(outputFileName, MiniJSON.Json.Serialize(duelData));
            }

            /*foreach (KeyValuePair<string, ZibFile> file in deckFiles)
            {
                DuelSeries series;
                if (!seenDecks.TryGetValue(file.Key, out series) || !usedDecks.Contains(file.Key))
                {
                    string name = file.Key.Substring(0, file.Key.Length - 4);
                    Dictionary<string, object> duelData = new Dictionary<string, object>();
                    duelData["name"] = name;
                    duelData["opponentDeck"] = "Decks/" + GetSeriesName(series) + "/" + name + ".json";
                    string outputFileName = Path.Combine(duelsDir, GetSeriesName(DuelSeries.None), name + ".json");
                    File.WriteAllText(outputFileName, MiniJSON.Json.Serialize(duelData));
                }
            }*/

            // TODO: Challenges
            foreach (CharData.Item character in Program.Manager.CharData.Items.Values)
            {
                //character.ChallengeDeckId
            }
        }

        static string SanitizeFileName(string name)
        {
            char[] invalids = Path.GetInvalidFileNameChars();
            return string.Join("_", name.Split(invalids, StringSplitOptions.RemoveEmptyEntries));
        }

        static void YdcToDictionary(ZibFile file, Dictionary<string, object> data)
        {
            List<int> mainDeckCards = new List<int>();
            List<int> extraDeckCards = new List<int>();
            List<int> sideDeckCards = new List<int>();
            data["m"] = new Dictionary<string, object>()
            {
                { "ids", mainDeckCards }
            };
            data["e"] = new Dictionary<string, object>()
            {
                { "ids", extraDeckCards }
            };
            data["s"] = new Dictionary<string, object>()
            {
                { "ids", sideDeckCards }
            };
            byte[] ydcDeckData = file.LoadBuffer();
            using (BinaryReader br = new BinaryReader(new MemoryStream(ydcDeckData)))
            {
                br.ReadInt64();// unknown
                ushort numMainDeckCards = br.ReadUInt16();
                for (int i = 0; i < numMainDeckCards; i++)
                {
                    mainDeckCards.Add(br.ReadUInt16());
                }
                ushort numExtraDeckCards = br.ReadUInt16();
                for (int i = 0; i < numExtraDeckCards; i++)
                {
                    extraDeckCards.Add(br.ReadUInt16());
                }
                ushort numSideDeckCards = br.ReadUInt16();
                for (int i = 0; i < numSideDeckCards; i++)
                {
                    sideDeckCards.Add(br.ReadUInt16());
                }
            }
        }
    }
}
