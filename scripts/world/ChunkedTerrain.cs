using Godot;

public partial class ChunkedTerrain : Node3D
{
	[Export] public int ChunkCountX = 3;
	[Export] public int ChunkCountZ = 3;
	[Export] public int ChunkResolution = 24;
	[Export] public float ChunkSize = 48.0f;
	[Export] public float HeightScale = 6.0f;
	[Export] public float NoiseFrequency = 0.045f;
	[Export] public int Seed = 1337;

	public override void _Ready()
	{
		Generate();
	}

	private void Generate()
	{
		foreach (Node child in GetChildren())
		{
			child.QueueFree();
		}

		FastNoiseLite noise = new FastNoiseLite
		{
			Seed = Seed,
			NoiseType = FastNoiseLite.NoiseTypeEnum.Perlin,
			FractalType = FastNoiseLite.FractalTypeEnum.Fbm,
			FractalOctaves = 4,
			FractalGain = 0.5f,
			FractalLacunarity = 2.0f,
			Frequency = NoiseFrequency
		};

		for (int cz = 0; cz < ChunkCountZ; cz++)
		{
			for (int cx = 0; cx < ChunkCountX; cx++)
			{
				CreateChunk(cx, cz, noise);
			}
		}
	}

	private void CreateChunk(int chunkX, int chunkZ, FastNoiseLite noise)
	{
		ArrayMesh mesh = BuildChunkMesh(chunkX, chunkZ, noise);

		StaticBody3D body = new StaticBody3D { Name = $"Chunk_{chunkX}_{chunkZ}" };
		MeshInstance3D meshInstance = new MeshInstance3D { Mesh = mesh };
		body.AddChild(meshInstance);

		CollisionShape3D collider = new CollisionShape3D();
		ConcavePolygonShape3D shape = new ConcavePolygonShape3D
		{
			Data = mesh.GetFaces()
		};
		collider.Shape = shape;
		body.AddChild(collider);

		AddChild(body);
	}

	private ArrayMesh BuildChunkMesh(int chunkX, int chunkZ, FastNoiseLite noise)
	{
		SurfaceTool st = new SurfaceTool();
		st.Begin(Mesh.PrimitiveType.Triangles);

		float step = ChunkSize / ChunkResolution;
		float originX = chunkX * ChunkSize;
		float originZ = chunkZ * ChunkSize;

		for (int z = 0; z < ChunkResolution; z++)
		{
			for (int x = 0; x < ChunkResolution; x++)
			{
				Vector3 v00 = VertexAt(originX + x * step, originZ + z * step, noise);
				Vector3 v10 = VertexAt(originX + (x + 1) * step, originZ + z * step, noise);
				Vector3 v01 = VertexAt(originX + x * step, originZ + (z + 1) * step, noise);
				Vector3 v11 = VertexAt(originX + (x + 1) * step, originZ + (z + 1) * step, noise);

				st.SetUV(new Vector2(0, 0));
				st.AddVertex(v00);
				st.SetUV(new Vector2(1, 0));
				st.AddVertex(v10);
				st.SetUV(new Vector2(0, 1));
				st.AddVertex(v01);

				st.SetUV(new Vector2(1, 0));
				st.AddVertex(v10);
				st.SetUV(new Vector2(1, 1));
				st.AddVertex(v11);
				st.SetUV(new Vector2(0, 1));
				st.AddVertex(v01);
			}
		}

		st.GenerateNormals();
		st.GenerateTangents();
		return st.Commit();
	}

	private Vector3 VertexAt(float x, float z, FastNoiseLite noise)
	{
		float y = noise.GetNoise2D(x, z) * HeightScale;
		return new Vector3(x, y, z);
	}
}
