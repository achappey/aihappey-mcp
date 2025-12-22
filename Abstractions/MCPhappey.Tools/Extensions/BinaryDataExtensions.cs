
namespace MCPhappey.Tools.Extensions;

public static class BinaryDataExtensions
{
    
    public static List<BinaryData> Split(this BinaryData data, int maxChunkSizeInMB)
    {
        long maxChunkSizeInBytes = maxChunkSizeInMB * 1024L * 1024L;
        List<BinaryData> chunks = [];

        // If the data is smaller than or equal to the max chunk size, return the original data
        if (data.ToMemory().Length <= maxChunkSizeInBytes)
        {
            chunks.Add(data);
            return chunks;
        }

        ReadOnlyMemory<byte> memory = data.ToMemory();
        long totalBytes = memory.Length;
        long offset = 0;

        while (offset < totalBytes)
        {
            int bytesToCopy = (int)Math.Min(maxChunkSizeInBytes, totalBytes - offset);
            ReadOnlyMemory<byte> chunkMemory = memory.Slice((int)offset, bytesToCopy);
            chunks.Add(new BinaryData(chunkMemory));
            offset += bytesToCopy;
        }

        return chunks;
    }

}
