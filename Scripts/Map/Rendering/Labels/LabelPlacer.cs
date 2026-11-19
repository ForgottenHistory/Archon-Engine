using System;
using System.Collections.Generic;
using UnityEngine;

namespace Map.Rendering.Labels
{
    /// <summary>A country to label: its text and which territory piece is its homeland.</summary>
    public struct LabelRequest
    {
        public ushort Country;
        public string Text;
        /// <summary>Province holding the capital (0 = none: the largest piece is the homeland).</summary>
        public ushort CapitalProvince;
        /// <summary>Text width / font size in the label font (letter spacing excluded).</summary>
        public float WidthRatio;
    }

    /// <summary>A placed label: text spread along Curve (map pixels, x right, y down).</summary>
    public sealed class PlacedLabel
    {
        public ushort Country;
        public string Text;
        /// <summary>Baseline-centre curve the letters are spread over, left to right as read.</summary>
        public Vector2[] Curve;
        /// <summary>Font size (em) in map pixels.</summary>
        public float Height;
        /// <summary>Length (map px) of the whole inside run the label sits in: how far it may grow when scaled up.</summary>
        public float Room;
    }

    /// <summary>Placement tuning (see MapLabelStyle for the data-file names).</summary>
    public sealed class LabelPlacementSettings
    {
        /// <summary>Territory pieces are found at 1/Downsample resolution.</summary>
        public int Downsample = 4;
        /// <summary>Skeleton pixels thinner than this fraction of the piece's max thickness are pruned.</summary>
        public float Trunk = 0.35f;
        /// <summary>Pieces smaller than this (downsampled cells) get no label.</summary>
        public int MinArea = 40;
        /// <summary>Other pieces are labelled if at least this fraction of the homeland's area.</summary>
        public float Detached = 0.3f;
        /// <summary>Largest font size, map pixels.</summary>
        public float MaxHeight = 88f;
        /// <summary>Straight candidates through the centre every AngleStep degrees.</summary>
        public int AngleStep = 15;
        /// <summary>Round shapes: score *= 1 - LevelPreference * |sin(angle)|.</summary>
        public float LevelPreference = 0.35f;
        /// <summary>Elongation (long / short axis) at or below which the full level preference applies.</summary>
        public float ElongationRound = 1.6f;
        /// <summary>Elongation at or above which labels freely follow the shape.</summary>
        public float ElongationLong = 2.4f;
        /// <summary>Score bonus for the skeleton arc, which follows the country's shape.</summary>
        public float ArcBonus = 1.1f;
        /// <summary>Text is spread over the central Fill of the candidate line.</summary>
        public float Fill = 0.75f;
        /// <summary>The centre line keeps at least Clear * font size from the territory edge.</summary>
        public float Clear = 0.55f;
        /// <summary>Pieces are measured at a resolution giving about this many cells across.</summary>
        public int TargetCells = 160;
    }

    /// <summary>
    /// ENGINE: Paradox-style country label placement (prototype: Tools/prototypes/labels in hoi4-remade).
    ///
    /// Per labelled territory piece (the capital's piece, plus big detached pieces):
    ///   1. mask at an adaptive resolution, exact distance to the edge
    ///   2. candidates: the skeleton trunk's longest path fitted with a smooth arc, and straight lines
    ///      at every AngleStep through the thickness-weighted centre
    ///   3. each candidate keeps its central Fill; font size = min(thickness along it / Clear,
    ///      length / text width, MaxHeight)
    ///   4. best score wins: size, times a horizontal preference that fades out for elongated shapes
    ///
    /// Pure C# on arrays: safe to run off the main thread. Holds the province id map; owners come
    /// per call, so ownership changes only re-place the affected countries.
    /// </summary>
    public sealed class LabelPlacer
    {
        private readonly ushort[] ids;   // province id per map pixel, row 0 = top
        private readonly int width, height;
        private int[] provinceFirstPixel; // lazily built: one pixel of each province (-1 = none)

        public LabelPlacer(ushort[] provinceIds, int mapWidth, int mapHeight)
        {
            ids = provinceIds ?? throw new ArgumentNullException(nameof(provinceIds));
            width = mapWidth;
            height = mapHeight;
            if (ids.Length != width * height)
                throw new ArgumentException("province id array does not match the map size");
        }

        public int Width => width;
        public int Height => height;

        /// <summary>
        /// Place labels for the requested countries. ownerOfProvince[id] = owning country (0 = none).
        /// Countries owning nothing yield no labels.
        /// </summary>
        public List<PlacedLabel> Place(ushort[] ownerOfProvince, IReadOnlyList<LabelRequest> requests, LabelPlacementSettings s)
        {
            var result = new List<PlacedLabel>();
            if (requests == null || requests.Count == 0) return result;
            s ??= new LabelPlacementSettings();
            EnsureProvinceFirstPixel();

            int ds = Mathf.Max(1, s.Downsample);
            var coarse = new CoarseGrid(this, ownerOfProvince, ds);

            foreach (var req in requests)
            {
                if (!coarse.ComponentsByOwner.TryGetValue(req.Country, out var comps) || comps.Count == 0)
                    continue;
                string text = req.Text;
                if (string.IsNullOrEmpty(text)) continue;
                float ratio = Mathf.Max(req.WidthRatio, 0.3f);

                int main = FindHomeland(coarse, comps, req, ownerOfProvince);
                int mainArea = coarse.Area[main];

                // Homeland first, then other pieces by size
                var order = new List<int>(comps);
                order.Sort((a, b) => coarse.Area[b].CompareTo(coarse.Area[a]));
                order.Remove(main);
                order.Insert(0, main);

                foreach (int comp in order)
                {
                    int area = coarse.Area[comp];
                    if (comp != main && (area < s.MinArea || area < s.Detached * mainArea)) continue;
                    if (area < s.MinArea / 4) continue;

                    var label = PlacePiece(coarse, comp, req.Country, text, ratio, ownerOfProvince, s);
                    if (label != null) result.Add(label);
                }
            }
            return result;
        }

        // ------------------------------------------------------------------ pieces

        private int FindHomeland(CoarseGrid coarse, List<int> comps, LabelRequest req, ushort[] owners)
        {
            int best = comps[0];
            foreach (int c in comps)
                if (coarse.Area[c] > coarse.Area[best]) best = c;

            ushort cap = req.CapitalProvince;
            if (cap == 0 || OwnerOf(owners, cap) != req.Country) return best;

            // Cells sampling the capital province, majority component
            var votes = new Dictionary<int, int>();
            for (int i = 0; i < coarse.Cells.Length; i++)
            {
                if (coarse.SampleIds[i] != cap) continue;
                int c = coarse.Component[i];
                if (c < 0) continue;
                votes.TryGetValue(c, out int n);
                votes[c] = n + 1;
            }
            if (votes.Count == 0 && cap < provinceFirstPixel.Length && provinceFirstPixel[cap] >= 0)
            {
                // Province too small to be sampled: the cell containing one of its pixels
                int p = provinceFirstPixel[cap];
                int cx = Mathf.Clamp((p % width) / coarse.Ds, 0, coarse.W - 1);
                int cy = Mathf.Clamp((p / width) / coarse.Ds, 0, coarse.H - 1);
                int c = coarse.Component[cy * coarse.W + cx];
                if (c >= 0 && coarse.Owner[c] == req.Country) votes[c] = 1;
            }
            int bestVotes = 0;
            foreach (var kv in votes)
                if (kv.Value > bestVotes && coarse.Owner[kv.Key] == req.Country) { bestVotes = kv.Value; best = kv.Key; }
            return best;
        }

        private PlacedLabel PlacePiece(CoarseGrid coarse, int comp, ushort country, string text, float ratio,
            ushort[] owners, LabelPlacementSettings s)
        {
            int ds = coarse.Ds;
            var bb = coarse.Bounds[comp]; // coarse cells: x0, y0, x1, y1 inclusive
            int fy0 = bb.y * ds, fy1 = (bb.w + 1) * ds;
            int fx0 = bb.x * ds, fx1 = (bb.z + 1) * ds;
            int step = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(fy1 - fy0, fx1 - fx0) / (float)Mathf.Max(1, s.TargetCells)), 1, ds);
            int pad = 2 * step;
            int y0 = Mathf.Max(fy0 - pad, 0), x0 = Mathf.Max(fx0 - pad, 0);
            int y1 = Mathf.Min(fy1 + pad, height), x1 = Mathf.Min(fx1 + pad, width);
            int half = step / 2;
            int fh = Mathf.Max(0, (y1 - (y0 + half) + step - 1) / step);
            int fw = Mathf.Max(0, (x1 - (x0 + half) + step - 1) / step);
            if (fw < 2 || fh < 2) return null;

            // Fine mask of the country around the piece
            var fine = new bool[fw * fh];
            for (int r = 0; r < fh; r++)
            {
                int py = y0 + half + r * step;
                int row = py * width;
                for (int c = 0; c < fw; c++)
                    fine[r * fw + c] = OwnerOf(owners, ids[row + x0 + half + c * step]) == country;
            }

            // Keep only this piece: the fine component overlapping the coarse piece most
            int[] flab = LabelComponents(fine, fw, fh, out int fn);
            if (fn == 0) return null;
            var hits = new int[fn + 1];
            for (int cy = bb.y; cy <= bb.w; cy++)
            for (int cx = bb.x; cx <= bb.z; cx++)
            {
                if (coarse.Component[cy * coarse.W + cx] != comp) continue;
                int r = Mathf.Clamp((cy * ds + ds / 2 - y0 - half) / step, 0, fh - 1);
                int c = Mathf.Clamp((cx * ds + ds / 2 - x0 - half) / step, 0, fw - 1);
                hits[flab[r * fw + c]]++;
            }
            int keep = 0;
            for (int i = 1; i <= fn; i++) if (hits[i] > hits[keep]) keep = i;
            if (keep == 0) return null;

            // Crop padded by one empty cell
            int w = fw + 2, h = fh + 2;
            var mask = new bool[w * h];
            for (int r = 0; r < fh; r++)
            for (int c = 0; c < fw; c++)
                mask[(r + 1) * w + c + 1] = flab[r * fw + c] == keep;
            int ox = x0 - step, oy = y0 - step;

            float[] dist = DistanceTransform(mask, w, h);
            var best = BestLayout(mask, dist, w, h, ratio, s.MaxHeight / step, s);
            if (best == null) return null;

            var curve = new Vector2[best.Value.curve.Length];
            for (int i = 0; i < curve.Length; i++)
                curve[i] = (best.Value.curve[i] + new Vector2(0.5f, 0.5f)) * step + new Vector2(ox, oy);
            return new PlacedLabel { Country = country, Text = text, Curve = curve, Height = best.Value.height * step,
                                     Room = best.Value.room * step };
        }

        private static ushort OwnerOf(ushort[] owners, ushort province) =>
            province < owners.Length ? owners[province] : (ushort)0;

        private void EnsureProvinceFirstPixel()
        {
            if (provinceFirstPixel != null) return;
            int max = 0;
            for (int i = 0; i < ids.Length; i++) if (ids[i] > max) max = ids[i];
            var first = new int[max + 1];
            for (int i = 0; i <= max; i++) first[i] = -1;
            for (int i = 0; i < ids.Length; i++) if (first[ids[i]] < 0) first[ids[i]] = i;
            provinceFirstPixel = first;
        }

        // ------------------------------------------------------------------ coarse grid

        private sealed class CoarseGrid
        {
            public readonly int Ds, W, H;
            public readonly ushort[] SampleIds;      // province sampled by each cell
            public readonly ushort[] Cells;          // owner per cell
            public readonly int[] Component;         // component per cell (-1 = unowned)
            public readonly List<ushort> Owner = new List<ushort>();
            public readonly List<int> Area = new List<int>();
            public readonly List<Vector4Int> Bounds = new List<Vector4Int>();
            public readonly Dictionary<ushort, List<int>> ComponentsByOwner = new Dictionary<ushort, List<int>>();

            public CoarseGrid(LabelPlacer p, ushort[] owners, int ds)
            {
                Ds = ds;
                int half = ds / 2;
                W = Mathf.Max(1, (p.width - half + ds - 1) / ds);
                H = Mathf.Max(1, (p.height - half + ds - 1) / ds);
                SampleIds = new ushort[W * H];
                Cells = new ushort[W * H];
                for (int y = 0; y < H; y++)
                {
                    int row = Mathf.Min(y * ds + half, p.height - 1) * p.width;
                    for (int x = 0; x < W; x++)
                    {
                        ushort id = p.ids[row + Mathf.Min(x * ds + half, p.width - 1)];
                        SampleIds[y * W + x] = id;
                        Cells[y * W + x] = OwnerOf(owners, id);
                    }
                }

                // 4-connected components of equal owner
                Component = new int[W * H];
                for (int i = 0; i < Component.Length; i++) Component[i] = -1;
                var stack = new Stack<int>();
                for (int start = 0; start < Cells.Length; start++)
                {
                    ushort o = Cells[start];
                    if (o == 0 || Component[start] >= 0) continue;
                    int id = Owner.Count;
                    int area = 0, bx0 = int.MaxValue, by0 = int.MaxValue, bx1 = -1, by1 = -1;
                    Component[start] = id;
                    stack.Push(start);
                    while (stack.Count > 0)
                    {
                        int i = stack.Pop();
                        int x = i % W, y = i / W;
                        area++;
                        if (x < bx0) bx0 = x; if (x > bx1) bx1 = x;
                        if (y < by0) by0 = y; if (y > by1) by1 = y;
                        if (x > 0) Visit(i - 1);
                        if (x < W - 1) Visit(i + 1);
                        if (y > 0) Visit(i - W);
                        if (y < H - 1) Visit(i + W);
                    }
                    Owner.Add(o);
                    Area.Add(area);
                    Bounds.Add(new Vector4Int(bx0, by0, bx1, by1));
                    if (!ComponentsByOwner.TryGetValue(o, out var list))
                        ComponentsByOwner[o] = list = new List<int>();
                    list.Add(id);

                    void Visit(int j)
                    {
                        if (Component[j] < 0 && Cells[j] == o)
                        {
                            Component[j] = id;
                            stack.Push(j);
                        }
                    }
                }
            }
        }

        private struct Vector4Int
        {
            public int x, y, z, w; // x0, y0, x1, y1
            public Vector4Int(int x, int y, int z, int w) { this.x = x; this.y = y; this.z = z; this.w = w; }
        }

        /// <summary>4-connected components of a mask; labels 1..count, 0 = background.</summary>
        private static int[] LabelComponents(bool[] mask, int w, int h, out int count)
        {
            var lab = new int[w * h];
            var stack = new Stack<int>();
            count = 0;
            for (int start = 0; start < mask.Length; start++)
            {
                if (!mask[start] || lab[start] != 0) continue;
                int id = ++count;
                lab[start] = id;
                stack.Push(start);
                while (stack.Count > 0)
                {
                    int i = stack.Pop();
                    int x = i % w, y = i / w;
                    if (x > 0 && mask[i - 1] && lab[i - 1] == 0) { lab[i - 1] = id; stack.Push(i - 1); }
                    if (x < w - 1 && mask[i + 1] && lab[i + 1] == 0) { lab[i + 1] = id; stack.Push(i + 1); }
                    if (y > 0 && mask[i - w] && lab[i - w] == 0) { lab[i - w] = id; stack.Push(i - w); }
                    if (y < h - 1 && mask[i + w] && lab[i + w] == 0) { lab[i + w] = id; stack.Push(i + w); }
                }
            }
            return lab;
        }

        // ------------------------------------------------------------------ distance transform

        /// <summary>Exact Euclidean distance from each inside cell to the nearest outside cell (Felzenszwalb).</summary>
        private static float[] DistanceTransform(bool[] mask, int w, int h)
        {
            const float Inf = 1e20f;
            var f = new float[w * h];
            for (int i = 0; i < f.Length; i++) f[i] = mask[i] ? Inf : 0f;

            int n = Mathf.Max(w, h);
            var line = new float[n];
            var outLine = new float[n];
            var v = new int[n];
            var z = new float[n + 1];

            for (int x = 0; x < w; x++) // columns
            {
                for (int y = 0; y < h; y++) line[y] = f[y * w + x];
                Edt1D(line, h, outLine, v, z);
                for (int y = 0; y < h; y++) f[y * w + x] = outLine[y];
            }
            for (int y = 0; y < h; y++) // rows
            {
                Array.Copy(f, y * w, line, 0, w);
                Edt1D(line, w, outLine, v, z);
                for (int x = 0; x < w; x++) f[y * w + x] = Mathf.Sqrt(outLine[x]);
            }
            return f;
        }

        /// <summary>1D squared distance transform: lower envelope of parabolas (Felzenszwalb and Huttenlocher).</summary>
        private static void Edt1D(float[] f, int n, float[] d, int[] v, float[] z)
        {
            int k = 0;
            v[0] = 0;
            z[0] = float.NegativeInfinity;
            z[1] = float.PositiveInfinity;
            for (int q = 1; q < n; q++)
            {
                double s;
                while (true)
                {
                    int p = v[k];
                    s = (((double)f[q] + (double)q * q) - ((double)f[p] + (double)p * p)) / (2.0 * (q - p));
                    if (s <= z[k] && k > 0) { k--; continue; }
                    break;
                }
                if (s <= z[k])
                {
                    // k == 0: the new parabola is lower everywhere
                    v[0] = q;
                    continue;
                }
                k++;
                v[k] = q;
                z[k] = (float)s;
                z[k + 1] = float.PositiveInfinity;
            }
            k = 0;
            for (int q = 0; q < n; q++)
            {
                while (z[k + 1] < q) k++;
                float dq = q - v[k];
                d[q] = dq * dq + f[v[k]];
            }
        }

        // ------------------------------------------------------------------ layout

        private struct Layout
        {
            public Vector2[] curve;
            public float length, height, room;
        }

        private Layout? BestLayout(bool[] mask, float[] dist, int w, int h, float ratio, float maxHeight, LabelPlacementSettings s)
        {
            var cands = new List<(Vector2[] curve, float bonus)>();

            var path = TrunkPath(mask, dist, w, h, s.Trunk);
            if (path != null && path.Count >= 2)
            {
                var arc = FitArc(path, dist, w, h);
                if (arc != null) cands.Add((arc, s.ArcBonus));
            }

            // Thickness-weighted centre and second moments
            double sw = 0, sx = 0, sy = 0;
            double mx = 0, my = 0, mxx = 0, myy = 0, mxy = 0;
            int count = 0, argmax = -1;
            float maxW = -1;
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float d = dist[y * w + x];
                if (d <= 0) continue;
                double wt = (double)d * d;
                sw += wt; sx += x * wt; sy += y * wt;
                count++;
                mx += x; my += y; mxx += (double)x * x; myy += (double)y * y; mxy += (double)x * y;
                if (d > maxW) { maxW = d; argmax = y * w + x; }
            }
            if (count < 3 || sw <= 0) return null;

            float cx = (float)(sx / sw) + 0.5f, cy = (float)(sy / sw) + 0.5f;
            int ix = Mathf.Clamp((int)cx, 0, w - 1), iy = Mathf.Clamp((int)cy, 0, h - 1);
            if (dist[iy * w + ix] < 1f)
            {
                cx = argmax % w + 0.5f;
                cy = argmax / w + 0.5f;
            }
            float r = Mathf.Sqrt(w * w + h * h);
            for (int deg = -90; deg < 90; deg += Mathf.Max(1, s.AngleStep))
            {
                float a = deg * Mathf.Deg2Rad;
                var u = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                var c = new Vector2(cx, cy);
                cands.Add((new[] { c - r * u, c + r * u }, 1f));
            }

            // Elongation from the covariance: round shapes read level, long ones follow their axis
            double vx = mxx / count - (mx / count) * (mx / count);
            double vy = myy / count - (my / count) * (my / count);
            double cxy = mxy / count - (mx / count) * (my / count);
            double tr = vx + vy, det = vx * vy - cxy * cxy;
            double disc = Math.Sqrt(Math.Max(tr * tr / 4 - det, 0));
            double l1 = tr / 2 + disc, l2 = Math.Max(tr / 2 - disc, 1e-6);
            float elong = (float)Math.Sqrt(l1 / l2);
            float range = Mathf.Max(s.ElongationLong - s.ElongationRound, 1e-3f);
            float pref = s.LevelPreference * Mathf.Clamp01((s.ElongationLong - elong) / range);

            Layout? best = null;
            float bestScore = -1;
            foreach (var (curve, bonus) in cands)
            {
                var lay = LayoutAlong(Orient(curve), dist, w, h, ratio, maxHeight, s);
                if (lay.height <= 0) continue;
                float score = bonus * lay.height * (1 - pref * Mathf.Abs(Mathf.Sin(AngleOf(lay.curve))));
                if (score > bestScore) { bestScore = score; best = lay; }
            }
            return best;
        }

        /// <summary>Centred layout over the central Fill of the curve's inside run, sized by the thickness along it.</summary>
        private static Layout LayoutAlong(Vector2[] curve, float[] dist, int w, int h, float ratio, float maxHeight, LabelPlacementSettings s)
        {
            var none = new Layout { curve = curve, length = 0, height = 0 };
            var pts = Resample(curve, 0.5f);
            if (pts.Count < 4) return none;

            var d = new float[pts.Count];
            for (int i = 0; i < pts.Count; i++) d[i] = Sample(dist, w, h, pts[i]);

            // Longest run with d >= 1
            int bestStart = -1, bestLen = 0;
            for (int i = 0; i < d.Length;)
            {
                if (d[i] < 1f) { i++; continue; }
                int j = i;
                while (j < d.Length && d[j] >= 1f) j++;
                if (j - i > bestLen) { bestLen = j - i; bestStart = i; }
                i = j;
            }
            if (bestStart < 0 || bestLen < 4) return none;

            int cut = (int)(bestLen * (1 - s.Fill) / 2);
            int a = bestStart + cut, n = bestLen - 2 * cut;
            if (n < 4) return none;

            var span = new Vector2[n];
            var ds = new float[n];
            for (int i = 0; i < n; i++) { span[i] = pts[a + i]; ds[i] = d[a + i]; }
            float length = (n - 1) * 0.5f;
            float height = Mathf.Min(Percentile(ds, 0.25f) / Mathf.Max(s.Clear, 1e-3f), length / ratio, maxHeight);
            return new Layout { curve = span, length = length, height = Mathf.Max(height, 0), room = (bestLen - 1) * 0.5f };
        }

        private static float Percentile(float[] values, float q)
        {
            var sorted = (float[])values.Clone();
            Array.Sort(sorted);
            float pos = q * (sorted.Length - 1);
            int lo = (int)pos;
            int hi = Mathf.Min(lo + 1, sorted.Length - 1);
            return Mathf.Lerp(sorted[lo], sorted[hi], pos - lo);
        }

        private static List<Vector2> Resample(Vector2[] curve, float step)
        {
            var result = new List<Vector2>();
            if (curve.Length == 0) return result;
            float total = 0;
            for (int i = 1; i < curve.Length; i++) total += Vector2.Distance(curve[i - 1], curve[i]);
            if (total <= 0) { result.Add(curve[0]); return result; }

            int seg = 0;
            float segStart = 0, segLen = Vector2.Distance(curve[0], curve[Mathf.Min(1, curve.Length - 1)]);
            for (float t = 0; t < total; t += step)
            {
                while (seg < curve.Length - 2 && t > segStart + segLen)
                {
                    segStart += segLen;
                    seg++;
                    segLen = Vector2.Distance(curve[seg], curve[seg + 1]);
                }
                float f = segLen > 0 ? Mathf.Clamp01((t - segStart) / segLen) : 0;
                result.Add(Vector2.Lerp(curve[seg], curve[seg + 1], f));
            }
            return result;
        }

        private static float Sample(float[] dist, int w, int h, Vector2 p)
        {
            int x = Mathf.Clamp((int)p.x, 0, w - 1), y = Mathf.Clamp((int)p.y, 0, h - 1);
            if (p.x < 0) x = 0;
            if (p.y < 0) y = 0;
            return dist[y * w + x];
        }

        /// <summary>Read left to right; exactly vertical reads bottom to top (y down).</summary>
        private static Vector2[] Orient(Vector2[] curve)
        {
            var d = curve[curve.Length - 1] - curve[0];
            float eps = 1e-3f * Mathf.Max(Mathf.Abs(d.y), 1f);
            if (d.x < -eps || (Mathf.Abs(d.x) <= eps && d.y > 0))
            {
                var r = (Vector2[])curve.Clone();
                Array.Reverse(r);
                return r;
            }
            return curve;
        }

        private static float AngleOf(Vector2[] curve)
        {
            var d = curve[curve.Length - 1] - curve[0];
            return Mathf.Atan2(d.y, d.x);
        }

        // ------------------------------------------------------------------ skeleton arc

        private static List<Vector2> TrunkPath(bool[] mask, float[] dist, int w, int h, float trunkFraction)
        {
            var skel = ZhangSuen(mask, w, h);
            float maxD = 0;
            for (int i = 0; i < dist.Length; i++) if (dist[i] > maxD) maxD = dist[i];
            float threshold = trunkFraction * maxD;

            var trunk = new bool[w * h];
            for (int i = 0; i < trunk.Length; i++) trunk[i] = skel[i] && dist[i] >= threshold;

            // 8-connected trunk components; keep the one maximizing max thickness * sqrt(size)
            var lab = new int[w * h];
            var stack = new Stack<int>();
            var members = new List<List<int>>();
            int bestComp = -1;
            float bestScore = -1;
            for (int start = 0; start < trunk.Length; start++)
            {
                if (!trunk[start] || lab[start] != 0) continue;
                var list = new List<int>();
                int id = members.Count + 1;
                float compMax = 0;
                lab[start] = id;
                stack.Push(start);
                while (stack.Count > 0)
                {
                    int i = stack.Pop();
                    list.Add(i);
                    if (dist[i] > compMax) compMax = dist[i];
                    int x = i % w, y = i / w;
                    for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        int nx = x + dx, ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                        int j = ny * w + nx;
                        if (trunk[j] && lab[j] == 0) { lab[j] = id; stack.Push(j); }
                    }
                }
                members.Add(list);
                float score = compMax * Mathf.Sqrt(list.Count);
                if (score > bestScore) { bestScore = score; bestComp = id; }
            }
            if (bestComp < 0) return null;

            var pts = members[bestComp - 1];
            int a = Farthest(pts[0], lab, bestComp, w, h, out _);
            Farthest(a, lab, bestComp, w, h, out var path);
            var result = new List<Vector2>(path.Count);
            foreach (int i in path) result.Add(new Vector2(i % w, i / w));
            return result;
        }

        /// <summary>BFS with geometric step lengths (the skeleton is nearly a tree); path from the farthest cell back.</summary>
        private static int Farthest(int start, int[] lab, int comp, int w, int h, out List<int> path)
        {
            var dist = new Dictionary<int, float> { [start] = 0f };
            var prev = new Dictionary<int, int> { [start] = -1 };
            var queue = new Queue<int>();
            queue.Enqueue(start);
            int end = start;
            float endDist = 0;
            while (queue.Count > 0)
            {
                int u = queue.Dequeue();
                int x = u % w, y = u / w;
                float du = dist[u];
                if (du > endDist) { endDist = du; end = u; }
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                    int v = ny * w + nx;
                    if (lab[v] != comp || dist.ContainsKey(v)) continue;
                    dist[v] = du + ((dx != 0 && dy != 0) ? 1.41421356f : 1f);
                    prev[v] = u;
                    queue.Enqueue(v);
                }
            }
            path = new List<int>();
            for (int p = end; p >= 0; p = prev[p]) path.Add(p);
            return end;
        }

        private static bool[] ZhangSuen(bool[] mask, int w, int h)
        {
            var img = new byte[w * h];
            for (int y = 1; y < h - 1; y++)
            for (int x = 1; x < w - 1; x++)
                img[y * w + x] = mask[y * w + x] ? (byte)1 : (byte)0;

            var remove = new List<int>();
            bool changed = true;
            while (changed)
            {
                changed = false;
                for (int step = 0; step < 2; step++)
                {
                    remove.Clear();
                    for (int y = 1; y < h - 1; y++)
                    for (int x = 1; x < w - 1; x++)
                    {
                        int i = y * w + x;
                        if (img[i] == 0) continue;
                        int p2 = img[i - w], p3 = img[i - w + 1], p4 = img[i + 1], p5 = img[i + w + 1];
                        int p6 = img[i + w], p7 = img[i + w - 1], p8 = img[i - 1], p9 = img[i - w - 1];
                        int b = p2 + p3 + p4 + p5 + p6 + p7 + p8 + p9;
                        if (b < 2 || b > 6) continue;
                        int transitions = (p2 == 0 && p3 == 1 ? 1 : 0) + (p3 == 0 && p4 == 1 ? 1 : 0) +
                                          (p4 == 0 && p5 == 1 ? 1 : 0) + (p5 == 0 && p6 == 1 ? 1 : 0) +
                                          (p6 == 0 && p7 == 1 ? 1 : 0) + (p7 == 0 && p8 == 1 ? 1 : 0) +
                                          (p8 == 0 && p9 == 1 ? 1 : 0) + (p9 == 0 && p2 == 1 ? 1 : 0);
                        if (transitions != 1) continue;
                        bool ok = step == 0
                            ? p2 * p4 * p6 == 0 && p4 * p6 * p8 == 0
                            : p2 * p4 * p8 == 0 && p2 * p6 * p8 == 0;
                        if (ok) remove.Add(i);
                    }
                    if (remove.Count > 0)
                    {
                        foreach (int i in remove) img[i] = 0;
                        changed = true;
                    }
                }
            }
            var result = new bool[w * h];
            for (int i = 0; i < img.Length; i++) result[i] = img[i] != 0;
            return result;
        }

        /// <summary>Quadratic y' = a x'^2 + b x' + c in the frame of the end-to-end axis, thickness weighted, bend limited.</summary>
        private static Vector2[] FitArc(List<Vector2> path, float[] dist, int w, int h)
        {
            Vector2 p0 = path[0], p1 = path[path.Count - 1];
            Vector2 axis = p1 - p0;
            float len = axis.magnitude;
            if (len < 1e-6f) return null;
            Vector2 u = axis / len;
            if (u.x < -1e-6f || (Mathf.Abs(u.x) <= 1e-6f && u.y > 0))
            {
                u = -u;
                (p0, p1) = (p1, p0);
            }
            var v = new Vector2(-u.y, u.x);

            // Weighted least squares: minimize sum (w_i (A_i coef - y_i))^2 -> normal equations
            double s00 = 0, s01 = 0, s02 = 0, s11 = 0, s12 = 0, s22 = 0, t0 = 0, t1 = 0, t2 = 0;
            float xmin = float.MaxValue, xmax = float.MinValue;
            foreach (var p in path)
            {
                Vector2 rel = p - p0;
                double x = Vector2.Dot(rel, u), y = Vector2.Dot(rel, v);
                double wt = Sample(dist, w, h, p) + 1e-3;
                double w2 = wt * wt;
                double a0 = x * x, a1 = x, a2 = 1;
                s00 += w2 * a0 * a0; s01 += w2 * a0 * a1; s02 += w2 * a0 * a2;
                s11 += w2 * a1 * a1; s12 += w2 * a1 * a2; s22 += w2 * a2 * a2;
                t0 += w2 * a0 * y; t1 += w2 * a1 * y; t2 += w2 * a2 * y;
                xmin = Mathf.Min(xmin, (float)x);
                xmax = Mathf.Max(xmax, (float)x);
            }
            if (!Solve3(s00, s01, s02, s11, s12, s22, t0, t1, t2, out double ca, out double cb, out double cc))
            {
                ca = 0; cb = 0; cc = 0;
            }

            // Limit bending: sagitta <= 18% of the chord
            double sag = Math.Abs(ca) * (len / 2) * (len / 2);
            if (sag > 0.18 * len) ca *= 0.18 * len / sag;

            const int Samples = 64;
            var result = new Vector2[Samples];
            for (int i = 0; i < Samples; i++)
            {
                float t = Mathf.Lerp(xmin, xmax, i / (float)(Samples - 1));
                float y = (float)(ca * t * t + cb * t + cc);
                result[i] = p0 + u * t + v * y;
            }
            return result;
        }

        /// <summary>Solve the symmetric 3x3 system [[a,b,c],[b,d,e],[c,e,f]] x = r (Cramer's rule).</summary>
        private static bool Solve3(double a, double b, double c, double d, double e, double f,
            double r0, double r1, double r2, out double x0, out double x1, out double x2)
        {
            double det = a * (d * f - e * e) - b * (b * f - e * c) + c * (b * e - d * c);
            if (Math.Abs(det) < 1e-12)
            {
                x0 = x1 = x2 = 0;
                return false;
            }
            x0 = (r0 * (d * f - e * e) - b * (r1 * f - e * r2) + c * (r1 * e - d * r2)) / det;
            x1 = (a * (r1 * f - e * r2) - r0 * (b * f - e * c) + c * (b * r2 - r1 * c)) / det;
            x2 = (a * (d * r2 - r1 * e) - b * (b * r2 - r1 * c) + r0 * (b * e - d * c)) / det;
            return true;
        }
    }
}
