using Godot;
using System;
using System.Collections.Generic;

public partial class CharacterVisualManager : Node3D
{
	[ExportGroup("Setup")]
	[Export] public NodePath MeshRootPath = new NodePath(".");
	[Export] public NodePath FallbackBodyMeshPath = new NodePath("BodyMesh");
	[Export] public bool ShowFallbackBodyWhenNoCharacter = false;
	[Export] public PackedScene DefaultCharacterScene;

	[ExportGroup("Model Offsets")]
	[Export] public float ModelYOffset = -0.01f;
	[Export] public bool AutoGroundModel = true;
	[Export] public float GroundExtraYOffset = 0.0f;
	[Export] public bool DynamicGrounding = true;
	[Export(PropertyHint.Range, "0.02,1.0,0.01")] public float GroundingUpdateInterval = 0.1f;
	[Export(PropertyHint.Range, "1.0,30.0,0.5")] public float GroundingLerpSpeed = 12.0f;
	[Export] public float ModelYawOffsetDeg = 180.0f;
	[Export] public Vector3 ModelScale = Vector3.One;

	[ExportGroup("Animation Names")]
	[Export] public string IdleAnimName = "idle";
	[Export] public string RunAnimName = "run";
	[Export] public string WalkAnimName = "walk";
	[Export] public string JumpAnimName = "jump";
	[Export] public string FallAnimName = "fall";
	[Export] public float MoveSpeedThreshold = 0.12f;
	[Export] public bool VerboseLogging = true;

	private Node3D _meshRoot;
	private MeshInstance3D _fallbackBodyMesh;
	private Node3D _activeCharacterModel;
	private AnimationPlayer _animPlayer;
	private Skeleton3D _skeleton;
	private string _idleClip = string.Empty;
	private string _runClip = string.Empty;
	private string _jumpClip = string.Empty;
	private string _fallClip = string.Empty;
	private string _currentClip = string.Empty;
	private float _groundingTimer = 0f;
	private float _currentAutoGroundOffset = 0f;

	public override void _Ready()
	{
		_meshRoot = ResolveMeshRoot();
		if (_meshRoot == null)
		{
			GD.PushWarning("[CharacterVisualManager] MeshRoot not found.");
			return;
		}

		_fallbackBodyMesh = _meshRoot.GetNodeOrNull<MeshInstance3D>(FallbackBodyMeshPath);
		SetFallbackBodyVisible(ShowFallbackBodyWhenNoCharacter);

		if (DefaultCharacterScene != null)
		{
			SetCharacterScene(DefaultCharacterScene);
		}
	}

	public override void _Process(double delta)
	{
		if (!AutoGroundModel || !DynamicGrounding || _activeCharacterModel == null)
		{
			return;
		}

		_groundingTimer -= (float)delta;
		if (_groundingTimer > 0f)
		{
			return;
		}

		_groundingTimer = Mathf.Max(0.02f, GroundingUpdateInterval);
		float targetAutoOffset = ComputeAutoGroundOffset();
		_currentAutoGroundOffset = Mathf.Lerp(
			_currentAutoGroundOffset,
			targetAutoOffset,
			Mathf.Clamp((float)delta * GroundingLerpSpeed, 0f, 1f));

		ApplyModelOffsets();
	}

	public bool SetCharacterScenePath(string resourcePath)
	{
		if (string.IsNullOrWhiteSpace(resourcePath))
		{
			GD.PushWarning("[CharacterVisualManager] Empty character resource path.");
			return false;
		}

		PackedScene scene = ResourceLoader.Load<PackedScene>(resourcePath);
		if (scene == null)
		{
			GD.PushWarning($"[CharacterVisualManager] Failed to load PackedScene at '{resourcePath}'.");
			return false;
		}

		return SetCharacterScene(scene);
	}

	public bool SetCharacterScene(PackedScene scene)
	{
		if (_meshRoot == null)
		{
			_meshRoot = ResolveMeshRoot();
			if (_meshRoot == null)
			{
				GD.PushWarning("[CharacterVisualManager] MeshRoot not found.");
				return false;
			}
		}

		if (scene == null)
		{
			GD.PushWarning("[CharacterVisualManager] Character PackedScene is null.");
			SetFallbackBodyVisible(ShowFallbackBodyWhenNoCharacter);
			return false;
		}

		ClearCharacter();

		Node instance = scene.Instantiate();
		if (instance == null)
		{
			GD.PushError("[CharacterVisualManager] Failed to instance character scene.");
			SetFallbackBodyVisible(ShowFallbackBodyWhenNoCharacter);
			return false;
		}

		if (instance is not Node3D model3D)
		{
			GD.PushError("[CharacterVisualManager] Instanced character root is not Node3D.");
			instance.QueueFree();
			SetFallbackBodyVisible(ShowFallbackBodyWhenNoCharacter);
			return false;
		}

		_meshRoot.AddChild(model3D);
		_activeCharacterModel = model3D;

		ApplyModelOffsets();
		ResolveModelComponents();
		ResolveAnimationClips();
		PlayClip(_idleClip, true);
		SetFallbackBodyVisible(false);
		_groundingTimer = 0f;
		return true;
	}

	public void ClearCharacter()
	{
		if (_activeCharacterModel != null && IsInstanceValid(_activeCharacterModel))
		{
			_activeCharacterModel.QueueFree();
		}

		_activeCharacterModel = null;
		_animPlayer = null;
		_skeleton = null;
		_idleClip = string.Empty;
		_runClip = string.Empty;
		_jumpClip = string.Empty;
		_fallClip = string.Empty;
		_currentClip = string.Empty;
		_groundingTimer = 0f;
		_currentAutoGroundOffset = 0f;
		SetFallbackBodyVisible(ShowFallbackBodyWhenNoCharacter);
	}

	private void SetFallbackBodyVisible(bool visible)
	{
		if (_fallbackBodyMesh != null && IsInstanceValid(_fallbackBodyMesh))
		{
			_fallbackBodyMesh.Visible = visible;
		}
	}

	public void SetLocomotionState(bool isGrounded, float horizontalSpeed, float verticalVelocity)
	{
		if (_animPlayer == null)
		{
			return;
		}

		string targetClip;
		if (!isGrounded)
		{
			targetClip = verticalVelocity > 0.05f ? _jumpClip : _fallClip;
			if (string.IsNullOrWhiteSpace(targetClip))
			{
				targetClip = _idleClip;
			}
		}
		else
		{
			targetClip = horizontalSpeed > MoveSpeedThreshold ? _runClip : _idleClip;
		}

		PlayClip(targetClip, false);
	}

	private Node3D ResolveMeshRoot()
	{
		if (MeshRootPath == null || MeshRootPath.IsEmpty)
		{
			return this;
		}

		if (MeshRootPath == new NodePath("."))
		{
			return this;
		}

		return GetNodeOrNull<Node3D>(MeshRootPath);
	}

	private void ApplyModelOffsets()
	{
		if (_activeCharacterModel == null)
		{
			return;
		}

		Vector3 pos = _activeCharacterModel.Position;
		float yOffset = ModelYOffset;
		if (AutoGroundModel)
		{
			yOffset += DynamicGrounding ? _currentAutoGroundOffset : ComputeAutoGroundOffset();
		}

		pos.Y = yOffset + GroundExtraYOffset;
		_activeCharacterModel.Position = pos;

		Vector3 rot = _activeCharacterModel.RotationDegrees;
		rot.Y = ModelYawOffsetDeg;
		_activeCharacterModel.RotationDegrees = rot;

		_activeCharacterModel.Scale = ModelScale;
	}

	private float ComputeAutoGroundOffset()
	{
		if (_activeCharacterModel == null)
		{
			return 0f;
		}

		float minY = float.PositiveInfinity;
		var stack = new Stack<Node>();
		stack.Push(_activeCharacterModel);

		while (stack.Count > 0)
		{
			Node node = stack.Pop();
			for (int i = 0; i < node.GetChildCount(); i++)
			{
				stack.Push(node.GetChild(i));
			}

			if (node is not MeshInstance3D meshInstance || meshInstance.Mesh == null)
			{
				continue;
			}

			Aabb aabb = meshInstance.Mesh.GetAabb();
			Transform3D toModel = _activeCharacterModel.GlobalTransform.AffineInverse() * meshInstance.GlobalTransform;

			Vector3 p = aabb.Position;
			Vector3 s = aabb.Size;
			Vector3[] corners =
			{
				new Vector3(p.X, p.Y, p.Z),
				new Vector3(p.X + s.X, p.Y, p.Z),
				new Vector3(p.X, p.Y + s.Y, p.Z),
				new Vector3(p.X, p.Y, p.Z + s.Z),
				new Vector3(p.X + s.X, p.Y + s.Y, p.Z),
				new Vector3(p.X + s.X, p.Y, p.Z + s.Z),
				new Vector3(p.X, p.Y + s.Y, p.Z + s.Z),
				new Vector3(p.X + s.X, p.Y + s.Y, p.Z + s.Z),
			};

			for (int c = 0; c < corners.Length; c++)
			{
				Vector3 modelSpaceCorner = toModel * corners[c];
				if (modelSpaceCorner.Y < minY)
				{
					minY = modelSpaceCorner.Y;
				}
			}
		}

		if (float.IsPositiveInfinity(minY))
		{
			return 0f;
		}

		return -minY;
	}

	private void ResolveModelComponents()
	{
		if (_activeCharacterModel == null)
		{
			return;
		}

		_animPlayer = _activeCharacterModel.FindChild("AnimationPlayer", true, false) as AnimationPlayer;
		_skeleton = _activeCharacterModel.FindChild("Skeleton3D", true, false) as Skeleton3D;

		if (_animPlayer == null)
		{
			GD.PushWarning("[CharacterVisualManager] AnimationPlayer missing on active model. T-pose likely.");
		}

		if (_skeleton == null)
		{
			GD.PushWarning("[CharacterVisualManager] Skeleton3D missing on active model. Retargeting unavailable.");
		}
	}

	private void ResolveAnimationClips()
	{
		if (_animPlayer == null)
		{
			return;
		}

		string[] clips = _animPlayer.GetAnimationList();
		if (VerboseLogging)
		{
			GD.Print($"[CharacterVisualManager] Clips: {string.Join(", ", clips)}");
		}

		_idleClip = ResolveClip(clips, IdleAnimName);
		_runClip = ResolveClip(clips, RunAnimName, WalkAnimName);
		_jumpClip = ResolveClip(clips, JumpAnimName);
		_fallClip = ResolveClip(clips, FallAnimName);

		if (string.IsNullOrWhiteSpace(_idleClip) && clips.Length > 0)
		{
			_idleClip = clips[0];
		}

		if (string.IsNullOrWhiteSpace(_runClip))
		{
			_runClip = _idleClip;
		}

		if (VerboseLogging)
		{
			GD.Print($"[CharacterVisualManager] Mapped clips -> idle='{_idleClip}', run='{_runClip}', jump='{_jumpClip}', fall='{_fallClip}'");
		}
	}

	private static string ResolveClip(string[] clips, params string[] hints)
	{
		if (clips == null || clips.Length == 0)
		{
			return string.Empty;
		}

		foreach (string hint in hints)
		{
			if (string.IsNullOrWhiteSpace(hint))
			{
				continue;
			}

			foreach (string clip in clips)
			{
				if (string.Equals(clip, hint, StringComparison.OrdinalIgnoreCase))
				{
					return clip;
				}
			}
		}

		foreach (string hint in hints)
		{
			if (string.IsNullOrWhiteSpace(hint))
			{
				continue;
			}

			string lowerHint = hint.ToLowerInvariant();
			foreach (string clip in clips)
			{
				if (clip.ToLowerInvariant().Contains(lowerHint))
				{
					return clip;
				}
			}
		}

		return string.Empty;
	}

	private void PlayClip(string clip, bool force)
	{
		if (_animPlayer == null || string.IsNullOrWhiteSpace(clip))
		{
			return;
		}

		if (!_animPlayer.HasAnimation(clip))
		{
			GD.PushWarning($"[CharacterVisualManager] Animation clip '{clip}' not found on model.");
			return;
		}

		if (!force && _currentClip == clip && _animPlayer.IsPlaying())
		{
			return;
		}

		_animPlayer.Play(clip, 0.15f);
		_currentClip = clip;
	}
}
