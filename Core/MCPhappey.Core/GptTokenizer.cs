using Microsoft.ML.Tokenizers;

namespace MCPhappey.Core;

public interface ITokenizer
{
    /// <summary>
    /// Splits the input text into a sequence of tokens.
    /// </summary>
    /// <param name="text">The input string to tokenize.</param>
    /// <returns>A list of tokens extracted from the input text.</returns>
    IReadOnlyList<int> Encode(string text);

    /// <summary>
    /// Joins a sequence of tokens into a single string.
    /// </summary>
    /// <param name="tokens">The sequence of tokens to join.</param>
    /// <returns>A string representation formed by concatenating the tokens.</returns>
    string Decode(IEnumerable<int> tokens);
}


public class GptTokenizer(Tokenizer tokenizer) : ITokenizer
{
    public IReadOnlyList<int> Encode(string text)
    {
        return tokenizer.EncodeToIds(text);
    }

    public string Decode(IEnumerable<int> tokens)
    {
        return tokenizer.Decode(tokens);
    }

    public string Tokenize(string text, int size)
    {
        return Decode(Encode(text).Take(size));
    }
}
