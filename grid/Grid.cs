using Godot;
using System;

public class Grid<T>
{
    public int Width { get; }
    public int Height { get; }
    public float CellSize { get; }
    public Vector3 Origin { get; }

    private T[,] _cells;

    public Grid(int width, int height, float cellSize, Vector3 origin)
    {
        Width = width; Height = height; CellSize = cellSize; Origin = origin;
        _cells = new T[width, height];
    }

    public Vector3 GetWorldCenter(int x, int z) =>
        Origin + new Vector3((x + 0.5f) * CellSize, 0, (z + 0.5f) * CellSize);

    public (int x, int z) GetCell(Vector3 worldPos) =>
        ((int)MathF.Floor((worldPos.X - Origin.X) / CellSize),
         (int)MathF.Floor((worldPos.Z - Origin.Z) / CellSize));

    public bool InBounds(int x, int z) => x >= 0 && x < Width && z >= 0 && z < Height;

    public T Get(int x, int z) => InBounds(x, z) ? _cells[x, z] : default;
    public T Get(Vector3 worldPos) { var (x, z) = GetCell(worldPos); return Get(x, z); }
    public void Set(int x, int z, T value) { if (InBounds(x, z)) _cells[x, z] = value; }

    public T[] GetAdjacentOrthogonal(int x, int z) => new[]
    {
        Get(x, z + 1), Get(x + 1, z), Get(x, z - 1), Get(x - 1, z)
    };
}
