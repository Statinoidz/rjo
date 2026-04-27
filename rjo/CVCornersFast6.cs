using UnityEngine;

public static class CVCornersFast6
{
    // Simple FAST-6 style corner test on a small circle.
    // img: grayscale [0..255], row-major, width w, height h
    public static bool IsCorner(byte[] img, int w, int h, int x, int y, byte threshold)
    {
        if (img == null) return false;
        if (x < 3 || y < 3 || x >= w - 3 || y >= h - 3) return false;

        int idx = y * w + x;
        byte p = img[idx];

        // 6 points on a radius-3 circle (hexagram-ish)
        // top, top-right, bottom-right, bottom, bottom-left, top-left
        int[,] offsets = new int[,]
        {
            {  0, -3 },
            {  3, -1 },
            {  3,  1 },
            {  0,  3 },
            { -3,  1 },
            { -3, -1 }
        };

        int brighter = 0;
        int darker = 0;

        for (int i = 0; i < 6; i++)
        {
            int ox = x + offsets[i, 0];
            int oy = y + offsets[i, 1];
            int oidx = oy * w + ox;
            byte q = img[oidx];

            if (q >= p + threshold) brighter++;
            if (q <= p - threshold) darker++;
        }

        // Corner if enough neighbors are consistently brighter or darker
        return brighter >= 4 || darker >= 4;
    }
}
