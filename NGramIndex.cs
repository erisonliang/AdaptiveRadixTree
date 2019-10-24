﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;


namespace System.Collections.Specialized
{
    /// <summary>
    ///     Allows n-gram indexing, used typically for O(1) sub-string searches.
    ///     ie: [abcd] => {abcd, abc, bcd, ab, bc, cd, a, b, c, d}
    /// </summary>
    public sealed class NGramIndex {
        private const char DEFAULT_WILDCARD_UNKNOWN  = '?';
        private const char DEFAULT_WILDCARD_ANYTHING = '*';

        private readonly Dictionary<string, List<NGram>> m_dict = new Dictionary<string, List<NGram>>();

        /// <summary>
        ///     Avoid too low values, as that will slow down matching.
        /// </summary>
        public readonly int MinNGramLength; // inclusive
        public readonly int MaxNGramLength; // inclusive
        public readonly DuplicateHandling Duplicates;
        /// <summary>?</summary>
        private readonly char WildcardUnknown;
        /// <summary>*</summary>
        private readonly char WildcardAnything;
        public int Count { get; private set; }

        #region constructors
        /// <param name="minNGramLength">Inclusive.</param>
        /// <param name="maxNGramLength">Inclusive.</param>
        public NGramIndex(int minNGramLength, int maxNGramLength, DuplicateHandling duplicates = DuplicateHandling.DuplicatesAllowed, char wildcard_unknown_character = DEFAULT_WILDCARD_UNKNOWN, char wildcard_anything_character = DEFAULT_WILDCARD_ANYTHING) {
            if(minNGramLength < 2) // while 1 is valid, it would be a massive slowdown
                throw new ArgumentOutOfRangeException(nameof(minNGramLength));
            if(maxNGramLength < minNGramLength)
                throw new ArgumentOutOfRangeException(nameof(maxNGramLength));

            this.MinNGramLength          = minNGramLength;
            this.MaxNGramLength          = maxNGramLength;
            this.Duplicates              = duplicates;
            this.WildcardUnknown         = wildcard_unknown_character;
            this.WildcardAnything        = wildcard_anything_character;
        }
        #endregion

        #region Add()
        /// <summary>
        ///     Average: O(k)    k = value.Length
        ///     Slightly slower if AllowDuplicates==false.
        ///     
        ///     Throws ArgumentNullException on null value.
        ///     Throws ArgumentException on empty/duplicate value.
        /// </summary>
        public void Add(string value) {
            if(value == null)
                throw new ArgumentNullException(nameof(value));
            if(value.Length == 0)
                throw new ArgumentException(nameof(value));

            using(var enumerator = this.GenerateNGram(value.Length).GetEnumerator()) {
                if(this.Duplicates == DuplicateHandling.NoDuplicates) {
                    if(!enumerator.MoveNext())
                        return;

                    var ngram        = enumerator.Current;
                    var ngram_string = value.Substring(ngram.Start, ngram.Length);

                    if(!m_dict.TryGetValue(ngram_string, out var list)) {
                        list = new List<NGram> {
                        new NGram(value, ngram.Start, ngram.Length)
                    };
                        m_dict.Add(ngram_string, list);
                    } else {
                        // check if the value exists
                        for(int i = 0; i < list.Count; i++) {
                            if(list[i].Value == value)
                                throw new ArgumentException($"The value ({value}) already exists.", nameof(value));
                        }
                        list.Add(new NGram(value, ngram.Start, ngram.Length));
                    }
                }

                while(enumerator.MoveNext()) {
                    var ngram        = enumerator.Current;
                    var ngram_string = value.Substring(ngram.Start, ngram.Length);

                    if(!m_dict.TryGetValue(ngram_string, out var list)) {
                        list = new List<NGram>();
                        m_dict.Add(ngram_string, list);
                    }

                    list.Add(new NGram(value, ngram.Start, ngram.Length));
                }
            }
            this.Count++;
        }
        #endregion
        #region AddRange()
        public void AddRange(IEnumerable<string> values) {
            foreach(var item in values)
                this.Add(item);
        }
        #endregion
        #region Remove()
        /// <summary>
        ///     Throws ArgumentNullException on null value.
        ///     Throws ArgumentException on empty/duplicate value.
        /// </summary>
        public bool Remove(string value) {
            if(value == null)
                throw new ArgumentNullException(nameof(value));
            if(value.Length == 0)
                throw new ArgumentException(nameof(value));

            // avoid expensive string comparison
            string reference = null;
            using(var enumerator = this.GenerateNGram(value.Length).GetEnumerator()) {
                if(!enumerator.MoveNext())
                    return false;

                // try find the first item so as to get the actual reference to the original string
                // this allows significant speedup in string comparison for all future compares
                var ngram        = enumerator.Current;
                var ngram_string = value.Substring(ngram.Start, ngram.Length);

                if(!m_dict.TryGetValue(ngram_string, out var list))
                    return false;

                int index  = 0;
                bool found = false;
                while(index < list.Count) {
                    var item = list[index];
                    if(item.Value != value)
                        index++;
                    else {
                        found = true;
                        reference = item.Value;
                        if(list.Count != 1)
                            list.RemoveAt(index);
                        else
                            m_dict.Remove(ngram_string);
                        break;
                    }
                }
                if(!found)
                    return false;

                while(enumerator.MoveNext()) {
                    ngram = enumerator.Current;
                    ngram_string = value.Substring(ngram.Start, ngram.Length);

                    if(!m_dict.TryGetValue(ngram_string, out list))
                        return false;

                    index = 0;
                    while(index < list.Count) {
                        var item = list[index];
                        // now that we have the original reference, all string comparisons can be avoided
                        // this is where most of the performance was lost
                        if(object.ReferenceEquals(item.Value, reference)) //if(item.Value != value)
                            index++;
                        else {
                            if(list.Count != 1)
                                list.RemoveAt(index);
                            else
                                m_dict.Remove(ngram_string);
                            break;
                        }
                    }
                }
            }
            this.Count--;
            return true;
        }
        #endregion
        #region RemoveRange()
        public void RemoveRange(IEnumerable<string> values) {
            foreach(var item in values)
                this.Remove(item);
        }
        #endregion
        #region Clear()
        /// <summary>
        ///     O(1)
        /// </summary>
        public void Clear() {
            m_dict.Clear();
            this.Count = 0;
        }
        #endregion
        #region ContainsFullValue()
        /// <summary>
        ///     Returns true if value was previously added.
        ///     
        ///     Throws ArgumentNullException on null value.
        ///     Throws ArgumentException on empty/duplicate value.
        /// </summary>
        public bool ContainsFullValue(string value) {
            if(value == null)
                throw new ArgumentNullException(nameof(value));
            if(value.Length == 0)
                throw new ArgumentException(nameof(value));

            if(!this.GenerateFirstNGram(value.Length, out var ngram))
                return false;
                
            var ngram_string = value.Substring(ngram.Start, ngram.Length);

            if(m_dict.TryGetValue(ngram_string, out var list)) {
                for(int i = 0; i < list.Count; i++) {
                    if(list[i].Value == value)
                        return true;
                }
            }

            return false;
        }
        #endregion
        #region GetItems()
        /// <summary>
        ///     O(n)
        ///     Returns the added items.
        ///     This is really inefficient and meant really only for debugging.
        /// </summary>
        public IEnumerable<string> GetItems() {
            return m_dict.Values.SelectMany(o => o).Select(o => o.Value).Distinct();
        }
        #endregion

        #region Search()
        /// <summary>
        ///     Searches for the given format which may include wildcard characters.
        /// </summary>
        /// <param name="format_including_wildcards">The user search format, which may include the wildcard characters.</param>
        public IEnumerable<string> Search(string format_including_wildcards, SearchOption match = SearchOption.ExactMatch) {
            // algorithm explanation:
            //
            // tldr; look at SearchSection(), repeat the same concept but across sections() matches
            //
            // format='*123***456?78?9*'     sections = { "123", "456?78?9" }
            // 
            // basically we search for sections {"123", "456?78?9"} and then do an intersection of results, 
            // since all of them need to be found
            //
            // Those searches do not focus on positioning being in-order, so we also need to manually verify at the end after intersecting
            // otherwise '45667889  123' would match all 2 sections, but wouldnt make sense with the search format
            //
            // since lookup() is O(1) on any of those, we prioritize least amount of results for intersection
            // ie: '123'=200 results, '456?78?9'=4000 results
            // so we process in that order
            // 
            // now there is no hard requirement to do intersection amongst multiple section searches, since we can always check preceding/following chars
            // as such, we balance the filtering by using the 2 approaches
            // keep in mind that the check for preceding/following chars must always happen whether you use intersection filtering or not
            

            var parsed = this.ParseSearchFormat(format_including_wildcards, match);

            var orderedSectionsIndexes = parsed.Sections
                .Select((value, index) => (value, index))
                .OrderByDescending(o => o.value.SearchLength)
                .ThenBy(o => o.value.ResultMustMatchAtStart ? 0 : 1)
                .ThenBy(o => o.value.ResultMustMatchAtEnd ? 0 : 1)
                .Select(o => o.index);

            int epoch              = 0;
            int intersection_count = int.MaxValue / 4;
            var results            = new Dictionary<string, EpochContainer>();
            foreach(var sectionIndex in orderedSectionsIndexes) {
                var sectionResults = this.SearchSection(
                    parsed, 
                    sectionIndex, 
                    original_string => epoch == 0 || (results.TryGetValue(original_string, out var epochContainer) && epochContainer.Epoch == epoch - 1))
                    .ToList();

                // ie: we either keep filtering by doing intersections, or we just try and compare the whole section
                // the rule of thumb here being that you can do roughly 4x dict compares in the time you verify the match (which will have to be done anyway)
                // keep in mind the further searches we go through, the more results we will get, thus filtering little
                // keep in mind we only care about the time savings, so this needs to filter a good portion of results
                // you could take into account the section.SearchLength and how many characters comparisons are avoided potentially if you want to try a smarter rule
                if(sectionResults.Count > intersection_count * 4)
                    break;

                // fast intersection
                
                if(epoch > 0) {
                    intersection_count = 0;
                    foreach(var sectionResult in sectionResults) {
                        if(results.TryGetValue(sectionResult, out var x) && x.Epoch == epoch - 1) {
                            x.Epoch = epoch;
                            intersection_count++;
                        }
                    }
                } else {
                    intersection_count = sectionResults.Count;
                    foreach(var sectionResult in sectionResults)
                        results.Add(sectionResult, new EpochContainer() { Epoch = 0 });
                }
                
                epoch++;
            }

            var regex = new WildcardRegex(
                format_including_wildcards, 
                match == SearchOption.ExactMatch ? WildcardRegex.SearchOption.ExactMatch : WildcardRegex.SearchOption.Partial, 
                this.WildcardUnknown, 
                this.WildcardAnything);
            //new System.Text.RegularExpressions.Regex(regex.ToRegex());

            var intersection = results
                .Where(o => o.Value.Epoch == epoch - 1)
                .Select(o => o.Key);

            foreach(var intersect in intersection) {
                if(regex.IsMatch(intersect))
                    yield return intersect;
            }
        }
        private sealed class EpochContainer {
            public int Epoch;
        }
        #endregion

        #region private SearchSection()
        /// <summary>
        ///     Searches for the strings that match the section.
        ///     ie: format='*123***456?78?9*'     sections = { "123", "456?78?9" }
        /// </summary>
        private IEnumerable<string> SearchSection(ParsedFormat format, int sectionIndex, Predicate<string> filter) {
            // algorithm explanation:
            // format='*123***456?78?9*'     sections = { "123", "456?78?9" }
            // section = "456?78?9"
            // 
            // basically we try and search for {'456', '78', '9'} and then do an intersection of results, 
            // since all of them need to be found
            //
            // Those searches do not focus on positioning being in-order, so we also need to manually verify at the end after intersecting
            // otherwise '789456' would match all 3 searches, but wouldnt make sense with the search format
            //
            // since lookup() is O(1) on any of those, we prioritize least amount of results for intersection
            // ie: '456'=200 results, '78'=4000 results, '9'=100000 results
            // so we process in that order
            // 
            // now there is no hard requirement to do intersection amongst multiple sub-section searches, since we can always check preceding/following chars
            // as such, we balance the filtering by using the 2 approaches
            // keep in mind that the check for preceding/following chars must always happen whether you use intersection filtering or not
            //
            // to make things more complicated, theres the case 
            // MaxNGramLength=5 
            // format='abcdeeefg?subsection*section2'
            // section = 'abcdeeefg?subsection'
            // 
            // we want to search for {'abcde', 'bcdee', 'cdeee', 'deeef', 'eeefg'}
            // this may seem redundant, but keep in mind the check for matches is O(1), and one combination might have a lot less results
            //
            // the alternative way to code this would be to use a RadixTree<string, List<NGram>> replacing Dictionary<string, List<NGram>>
            // and simply use a directed search 
            //var section_string = format.Format.Substring(section.SearchStart - section.WildcardUnknownBefore, section.WildcardUnknownBefore + section.SearchLength + section.WildcardUnknownAfter);
            //var results_before_verify = new AdaptiveRadixTree<string, List<NGram>>().WildcardMatchValues(section_string, this.WildcardUnknown);

            var section = format.Sections[sectionIndex];

            var sub_searches = this.SplitPosition(format.Format, section.SearchStart, section.SearchLength, this.WildcardUnknown) 
                .Where(o => o.length >= this.MinNGramLength)
                .SelectMany(o => this.MaxNGrams(o.start, o.length))
                .Select(ngram => {
                    m_dict.TryGetValue(format.Format.Substring(ngram.start, ngram.len), out var list);
                    return (ngram.start, list);
                })
                // avoid case where you get duplicated search patterns
                .GroupBy(o => o.list)
                .Select(o => o.First())
                .OrderBy(o => o.list?.Count ?? 0)
                .ToList();

            if(sub_searches.Count == 0 || sub_searches[0].list == null)
                yield break;

            var searchFormatIndex = sub_searches[0].start;
            var dict = sub_searches[0].list
                .Where(o => o.Value.Length >= format.TotalCharacters && filter(o.Value))
                .GroupBy(o => o.Value)
                .ToDictionary(o => o.Key, o => new Matches(){ SearchFormatIndex = searchFormatIndex, NGrams = o.ToList(), Epoch = -1 });
                
            // then do the intersection between those
            int epoch = 0;
            int intersection_count = dict.Count;
            for(int i = 1; i < sub_searches.Count; i++) {
                var sub_search = sub_searches[i];

                // ie: we either keep filtering by doing intersections, or we just try and compare the whole section
                // the rule of thumb here being that you can do roughly 4x dict compares in the time you verify the match (which will have to be done anyway)
                // keep in mind the further searches we go through, the more results we will get, thus filtering little
                // keep in mind we only care about the time savings, so this needs to filter a good portion of results
                // you could take into account the section.SearchLength and how many characters comparisons are avoided potentially if you want to try a smarter rule
                if(sub_search.list.Count > intersection_count * 4)
                    break;

                intersection_count = 0;
                foreach(var item in sub_search.list) {
                    if(dict.TryGetValue(item.Value, out var x) && x.Epoch == epoch - 1) {
                        x.Epoch = epoch;
                        intersection_count++;
                        //x.NGrams.Add(item);
                    }
                }
                epoch++;
            }

            // from the intersected results, make sure at least one ngram per Dict.KeyValuePair passes the format
            var intersection = dict.Values.Where(v => v.Epoch == epoch - 1);
            foreach(var potential_match in intersection) {
                int max = potential_match.NGrams.Count;
                for(int i = 0; i < max; i++) {
                    var ngram           = potential_match.NGrams[i];
                    var original_string = potential_match.Value;

                    if(ngram.Start < section.MinCharsBefore)
                        continue;
                    if(original_string.Length - (ngram.Start + ngram.Length) < section.MinCharsAfter)
                        continue;
                    if(section.ResultMustMatchAtStart && ngram.Start != 0)
                        continue;
                    if(section.ResultMustMatchAtEnd && ngram.Start + ngram.Length != original_string.Length)
                        continue;

                    // make sure that the section matches the search format

                    // check preceding characters
                    // ex: 'zz*123?xxxxx' will check '123?' after 'xxxxx' match
                    var valid     = true;
                    int max2      = potential_match.SearchFormatIndex;
                    int readIndex = ngram.Start - (max2 - section.SearchStart);

                    for(int j = section.SearchStart; j < max2; j++) {
                        var searchFormatChar = format.Format[j];
                        var c                = original_string[readIndex++];

                        if(searchFormatChar != this.WildcardUnknown && c != searchFormatChar) {
                            valid = false;
                            break;
                        }
                    }
                    if(!valid)
                        continue;

                    // check following characters
                    // ex: 'xxxxx?123??*zz' will check '?123??' after 'xxxxx' match
                    max2      = section.SearchStart + section.SearchLength;
                    readIndex = ngram.Start + ngram.Length;

                    for(int j = potential_match.SearchFormatIndex + ngram.Length; j < max2; j++) {
                        var searchFormatChar = format.Format[j];
                        var c                = original_string[readIndex++];

                        if(searchFormatChar != this.WildcardUnknown && c != searchFormatChar) {
                            valid = false;
                            break;
                        }
                    }
                    if(!valid)
                        continue;

                    // as soon as one match is found within ngram, no need to check further
                    yield return original_string;
                    break;
                }
            }
        }
        private sealed class Matches {
            public List<NGram> NGrams;
            public int Epoch;
            public int SearchFormatIndex;
            public string Value => this.NGrams[0].Value; // they all have the same value
        }
        #endregion
        #region private MaxNGrams()
        private IEnumerable<(int start, int len)> MaxNGrams(int start, int length) {
            var len = Math.Min(length, this.MaxNGramLength);
            int max = start + length;
            while(start + len <= max) {
                yield return (start, len);
                start++;
            }
        }
        #endregion
        #region private SplitPosition()
        /// <summary>
        ///     Same as string.Split(), but for returns positions instead.
        ///     ex: "abcde".SplitPosition(1, 4, new []{'b'}) = {(2,3)}
        /// </summary>
        private IEnumerable<(int start, int length)> SplitPosition(string source, int startIndex, int length, char separator) {
            int index = 0;
            int start = startIndex;
            int max   = startIndex + length;
            for(int i = startIndex; i < max; i++) {
                var c = source[i];
                if(c == separator) {
                    yield return (start, index - start);
                    start = index + 1;
                }
                index++;
            }
            yield return (start, index - start);
        }
        #endregion

        #region private ParseSearchFormat()
        private ParsedFormat ParseSearchFormat(string format, SearchOption match = SearchOption.ExactMatch) {
            var sections = ParseSearchFormatSections(format)
                .Where(o => o.len > 0) // avoids empty sections in cases such as 'aa**aa', '*aa' and 'aa*'
                .Select(o => new ConsecutiveParseSection(){ SearchStart = o.start, SearchLength = o.len })
                .ToList();

            for(int i = 0; i < sections.Count; i++) {
                var section = sections[i];
                
                // TrimStart(this.WildcardUnknown)
                while(section.SearchLength > 0 && format[section.SearchStart] == this.WildcardUnknown) {
                    section.SearchLength--;
                    section.SearchStart++;
                    section.WildcardUnknownBefore++;
                    section.MinCharsBefore++;
                }
                // TrimEnd(this.WildcardUnknown)
                while(section.SearchLength > 0 && format[section.SearchStart + section.SearchLength - 1] == this.WildcardUnknown) {
                    section.SearchLength--;
                    section.WildcardUnknownAfter++;
                    section.MinCharsAfter++;
                }

                if(i > 0) {
                    var prev = sections[i - 1];
                    section.MinCharsBefore += prev.SearchLength + prev.MinCharsBefore + prev.MinCharsAfter;
                }
            }
            for(int i = sections.Count - 2; i >= 0; i--) {
                var next = sections[i + 1];
                sections[i].MinCharsAfter += next.SearchLength + next.MinCharsBefore + next.MinCharsAfter;
            }

            if(match == SearchOption.ExactMatch) {
                sections[0].ResultMustMatchAtStart                = format[0] != this.WildcardAnything;
                sections[sections.Count - 1].ResultMustMatchAtEnd = format[format.Length - 1] != this.WildcardAnything;
            }

            // merge '??' section with prev
            // ex: 'abc*??*456' -> 'abc??*456'
            int index = 1;
            while(index < sections.Count) {
                var section = sections[index];
                if(section.SearchLength == 0 && !section.ResultMustMatchAtStart && !section.ResultMustMatchAtEnd && (section.WildcardUnknownBefore > 0 || section.WildcardUnknownAfter > 0)) {
                    sections[index - 1].WildcardUnknownAfter += section.WildcardUnknownBefore + section.WildcardUnknownAfter;
                    sections.RemoveAt(index);
                } else 
                    index++;
            }
            // move ['??' at section start] to [previous section end] for faster parse
            // ex: 'abc?*??456' -> 'abc???*456'
            index = 1;
            while(index < sections.Count) {
                var section = sections[index];
                if(section.WildcardUnknownBefore > 0) {
                    sections[index - 1].WildcardUnknownAfter += section.WildcardUnknownBefore;
                    section.WildcardUnknownBefore = 0;
                }
                index++;
            }

            return new ParsedFormat() {
                Format          = format,
                Sections        = sections,
                TotalCharacters = sections[0].SearchLength + sections[0].MinCharsBefore + sections[0].MinCharsAfter,
            };
        }
        /// <summary>
        ///     basically does format.Split(WildcardAnything)
        /// </summary>
        private IEnumerable<(int start, int len)> ParseSearchFormatSections(string format) {
            int start = 0;
            int len   = 0;

            for(int i = 0; i < format.Length; i++) {
                var c = format[i];
                len++;

                if(c == this.WildcardAnything) {
                    if(len > 1)
                        yield return (start, len - 1);

                    start = i + 1;
                    len   = 0;
                }
            }
            if(len > 0)
                yield return (start, len);
        }
        private sealed class ParsedFormat {
            /// <summary>
            ///     Unmodified user-specified search format.
            /// </summary>
            public string Format;
            public int TotalCharacters;
            public List<ConsecutiveParseSection> Sections;
        }
        /// <summary>
        ///     Represents a section of consecutive characters without any WILDCARD_ANYTHING in it.
        ///     This may include multiple WILDCARD_UNKNOWN.
        /// </summary>
        private sealed class ConsecutiveParseSection {
            /// <summary>
            ///     The start position within the search format of the ngram to search for.
            /// </summary>
            public int SearchStart;
            /// <summary>
            ///     The length within the search format of the ngram to search for.
            /// </summary>
            public int SearchLength;
            /// <summary>
            ///     How many characters before SearchStart within search format to continue matching.
            ///     ie: how many WILDCARD_UNKNOWN are at the start of the section.
            /// </summary>
            public int WildcardUnknownBefore;
            /// <summary>
            ///     How many characters after {SearchStart+SearchLength} within search format to continue matching.
            ///     ie: how many WILDCARD_UNKNOWN are at the end of the section.
            /// </summary>
            public int WildcardUnknownAfter;
            /// <summary>
            ///     How many characters before SearchStart must exist in the match.
            ///     ex: search format 'prev_section*current_section' -> length('prev_section') = 12
            /// </summary>
            public int MinCharsBefore;
            /// <summary>
            ///     How many characters after {SearchStart+SearchLength} must exist in the match.
            ///     ex: search format 'current_section*next_section' -> length('next_section') = 12
            /// </summary>
            public int MinCharsAfter;
            /// <summary>
            ///     Indicates result/match must start at 0.
            /// </summary>
            public bool ResultMustMatchAtStart;
            /// <summary>
            ///     Indicates result/match must end at comparand.Length.
            /// </summary>
            public bool ResultMustMatchAtEnd;
        }
        #endregion


        // maybe replace dict by radix/btree and use btree.startswith()

        ///// <summary>
        /////     Transforms '?123?456?789?' -> {'?123', '123?456', '456?789', '789?'}
        /////     Does not take into account min/max ngram settings on purpose.
        ///// </summary>
        //private IEnumerable<(int start, int len, int wildcard)> ComputeSubSectionNGrams(ParsedFormat format, int sectionIndex) {
        //    var section = format.Sections[sectionIndex];
        //
        //    int start = section.SearchStart - (section.WildcardUnknownBefore > 0 ? 1 : 0);
        //    int len   = section.SearchLength + (section.WildcardUnknownBefore > 0 ? 1 : 0) + (section.WildcardUnknownAfter > 0 ? 1 : 0);
        //
        //    var wildcards = new List<int>();
        //    for(int i = 0; i < len; i++) {
        //        var c = format.Format[start + i];
        //        if(c == this.WildcardUnknown)
        //            wildcards.Add(i);
        //    }
        //
        //    var max = wildcards.Count;
        //    for(int i = 0; i < max; i++) {
        //        var wildcard = wildcards[i];
        //        
        //    }
        //}


            //CREATE INDEX titles_trigrams_gin_idx ON titles USING GIN(trigrams_vector(title));
            // SELECT COUNT(*) FROM titles WHERE trigrams_vector(title) @@ trigrams_query('adventures');
            //CREATE OR REPLACE FUNCTION trigrams_array(word text)
            //        RETURNS text[]
            //        IMMUTABLE STRICT
            //        LANGUAGE "plpgsql"
            //AS $$
            //        DECLARE
            //                result text[];
            //        BEGIN
            //                FOR i IN 1 .. length(word) - 2 LOOP
            //                        result := result || quote_literal(substr(lower(word), i, 3));
            //                END LOOP;
            //
            //                RETURN result;
            //        END;
            //$$;
            //CREATE OR REPLACE FUNCTION trigrams_vector(text)
            //        RETURNS tsvector
            //        IMMUTABLE STRICT
            //        LANGUAGE "SQL"
            //AS $$
            //        SELECT array_to_string(trigrams_array($1), ' ')::tsvector;
            //$$;
            //CREATE OR REPLACE FUNCTION trigrams_query(text)
            //        RETURNS tsquery
            //        IMMUTABLE STRICT
            //        LANGUAGE "SQL"
            //AS $$
            //        SELECT array_to_string(trigrams_array($1), ' & ')::tsquery;
            //$$;

        #region private GenerateNGrams()
        /// <summary>
        ///     Decomposes the input into all n-gram variations.
        ///     ie: [abcd] => {abcd, abc, bcd, ab, bc, cd, a, b, c, d}
        ///     
        ///     This is typically used for efficient sub-string searching.
        ///     Could also be used as an alternative to a Generalized Suffix Tree for string searching.
        /// </summary>
        /// <param name="length">The string.Length you wish to decompose.</param>
        private IEnumerable<InternalNGram> GenerateNGram(int length) {
            int max = this.MaxNGramLength;
            if(max > length)
                max = length;
            
            // note: reverse-order is intentional, as that yields better performance (ie: more initial filtering)
            for(int n = max; n >= this.MinNGramLength; n--) {
                int count = length - n;
                for(int i = 0; i < count; i++)
                    yield return new InternalNGram(i, n);
            }
        }
        private readonly struct InternalNGram {
            public readonly int Start;
            public readonly int Length;
            public InternalNGram(int start, int length) {
                this.Start  = start;
                this.Length = length;
            }
        }
        #endregion
        #region private GenerateFirstNGram()
        private bool GenerateFirstNGram(int length, out InternalNGram result) {
            int max = this.MaxNGramLength;
            if(max > length)
                max = length;

            // note: reverse-order is intentional, as that yields better performance (ie: more initial filtering)
            if(max >= this.MinNGramLength) {
                result = new InternalNGram(0, max);
                return true;
            } else {
                result = default;
                return false;
            }
        }
        #endregion

        public enum SearchOption {
            /// <summary>
            ///     equivalent to "value = 'searchstring'"
            /// </summary>
            ExactMatch,
            /// <summary>
            ///     equivalent to "value LIKE '%searchstring%'"
            /// </summary>
            Partial,
        }
        private sealed class NGram {
            public readonly string Value;
            public readonly int Start;
            public readonly int Length;

            public NGram(string value, int start, int length) {
                this.Value  = value;
                this.Start  = start;
                this.Length = length;
            }
        }
        public enum DuplicateHandling {
            NoDuplicates,
            /// <summary>
            ///     Faster Add() due to no checks.
            /// </summary>
            DuplicatesAllowed,
        }
    }
}
