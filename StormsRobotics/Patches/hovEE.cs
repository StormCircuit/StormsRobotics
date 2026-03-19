using System;
using System.Collections.Generic;
using System.Threading;
using Assets.Scripts.Atmospherics;
using Assets.Scripts.GridSystem;
using Assets.Scripts.Localization2;
using Assets.Scripts.Networking;
using Assets.Scripts.Networks;
using Assets.Scripts.Objects.Electrical;
using Assets.Scripts.Objects.Entities;
using Assets.Scripts.Objects.Items;
using Assets.Scripts.Objects.Motherboards;
using Assets.Scripts.Objects.Pipes;
using Assets.Scripts.Util;
using Assets.Scripts.Vehicles;
using Cysharp.Threading.Tasks;
using TerrainSystem;
using Trading;
using UnityEngine;
using Util;

namespace Assets.Scripts.Objects;

public class RobotCargoHover : WheeledBase, IBatteryPowered, IPowered, IDensePoolable, IReferencable, IEvaluable, ICircuitHolder, IMiningTool, ITransmitable, ILogicable, IRepairable, IGenerateMinables
{
	public static float RepairSpeedScale = 0.4f;

	[Header("Hovee")]
	public static List<RobotMining> AllRobots = new List<RobotMining>();

	public Collider ContentsPanel;

	private List<ILogicable> _logicList = new List<ILogicable>(20);

	private float _targetx;

	private float _targety;

	private float _targetz;

	[SerializeField]
	protected float StateTimer;

	private float _unloadTimeout;

	public Transform Lense;

	private float _roamTimeout;

	public GameObject WheelsT;

	protected Human Target;

	protected float Delta;

	public float Power = 0.01f;

	public float MaxSpeed = 3f;

	public static float TurnSpeed = 100f;

	private float _reversing;

	private List<Slot> _storageSlots = new List<Slot>();

	[Header("PathFinding")]
	[ReadOnly]
	public List<GridPathfinder.NpcPathGrid> PathList;

	protected bool IsBusy;

	protected Vector3 AimVector;

	protected Vector3 TargetGrid;

	protected float StationaryTime;

	protected float StationaryTolerance;

	protected float LastPathChange;

	public static string[] RobotModeStrings = Enum.GetNames(typeof(RobotMode));

	private int _codeErrorState;

	private Vector3 _lastCheckPos;

	private readonly float _degreesToRadians = MathF.PI / 180f;

	private readonly int EngineAudioHash = Animator.StringToHash("Engine");

	private bool _engineSound;

	public float LerpSpeed = 5f;

	private float _enginePitch;

	private CancellationTokenWrapper _mineCancellation = new CancellationTokenWrapper();

	private Grid3 _currentPathTarget;

	public float RepairRatio => DamageState.TotalRatio;

	public ulong LastEditedBy { get; set; }

	[ByteArraySync]
	public float TargetX
	{
		get
		{
			return _targetx;
		}
		set
		{
			_targetx = value;
			if (NetworkManager.IsServer)
			{
				base.NetworkUpdateFlags |= 256;
			}
		}
	}

	[ByteArraySync]
	public float TargetY
	{
		get
		{
			return _targety;
		}
		set
		{
			_targety = value;
			if (NetworkManager.IsServer)
			{
				base.NetworkUpdateFlags |= 256;
			}
		}
	}

	[ByteArraySync]
	public float TargetZ
	{
		get
		{
			return _targetz;
		}
		set
		{
			_targetz = value;
			if (NetworkManager.IsServer)
			{
				base.NetworkUpdateFlags |= 256;
			}
		}
	}

	public Vector3 TargetPosition => new Vector3(TargetX, TargetY, TargetZ);

	public bool IsStorageFull
	{
		get
		{
			foreach (Slot storageSlot in _storageSlots)
			{
				if (!storageSlot.Occupant)
				{
					return false;
				}
			}
			return true;
		}
	}

	public override string[] ModeStrings => RobotModeStrings;

	public Slot BatterySlot => Slots[0];

	public BatteryCell Battery => BatterySlot.Occupant as BatteryCell;

	private bool HasBatteries => Battery;

	public Slot ProgrammableChipSlot => Slots[1];

	public ProgrammableChip ProgrammableChip => ProgrammableChipSlot.Occupant as ProgrammableChip;

	public bool IsOperable
	{
		get
		{
			if (_codeErrorState == 0 && ProgrammableChip != null)
			{
				return !ProgrammableChip.CompilationError;
			}
			return false;
		}
	}

	public Grid3 CurrentTargetGrid
	{
		get
		{
			if (PathList == null || PathList.Count == 0)
			{
				return GridPosition;
			}
			return PathList[0].Grid;
		}
	}

	public bool EngineSound
	{
		get
		{
			return _engineSound;
		}
		set
		{
			if (value == _engineSound)
			{
				if (value)
				{
					SetEngineAudio();
				}
				return;
			}
			_engineSound = value;
			if (value)
			{
				PlaySound(EngineAudioHash);
				SetEngineAudio();
			}
			else
			{
				StopSound(EngineAudioHash);
			}
		}
	}

	private bool InvalidPath
	{
		get
		{
			if (PathList != null)
			{
				return PathList.Count == 0;
			}
			return true;
		}
	}

	public override Vector3 CenterPosition => Bounds.center + base.Position;

	public CursorVoxelMode CursorVoxelMode => CursorVoxelMode.Default;

	public Vector3Int MinablesGenerationRange => GameConstants.MINABLES_GENERATION_RANGE_AIMEE;

	public Vector3 PreviousMinableRequestPosition { get; set; }

	public bool ShouldGenerate => base.ParentSlot == null;

	public Vector3 GeneratePosition => base.Position;

	public void HasPut()
	{
	}

	public List<ILogicable> GetBatchOutput()
	{
		for (int i = 0; i < Slots.Count; i++)
		{
			Slot slot = Slots[i];
			_logicList[i] = slot.Occupant as ILogicable;
		}
		return _logicList;
	}

	public override DelayedActionInstance AttackWith(Attack attack, bool doAction = true)
	{
		if (!attack.SourceItem)
		{
			return null;
		}
		if (attack.SourceItem is IRobotRepairer robotRepairer)
		{
			float num = robotRepairer.RepairQuantity(this);
			DelayedActionInstance delayedActionInstance = new DelayedActionInstance
			{
				Duration = num * robotRepairer.GetRepairSpeed() * RepairSpeedScale,
				ActionMessage = GameStrings.ActionRepairRobot.DisplayString
			};
			if (DamageState.TotalRatio <= float.Epsilon)
			{
				return delayedActionInstance.Fail(GameStrings.StructureIsNotDamaged, ToTooltip());
			}
			if (!doAction)
			{
				return delayedActionInstance;
			}
			robotRepairer.Repair(base.netId, num * attack.CompletedRatio);
			return delayedActionInstance;
		}
		return base.AttackWith(attack, doAction);
	}

	public async UniTask HaltAndCatchFire()
	{
		if (!GameManager.RunSimulation)
		{
			return;
		}
		if (GameManager.IsThread)
		{
			await UniTask.SwitchToMainThread();
			if (ThingTransform.GetCancellationTokenOnDestroy().IsCancellationRequested)
			{
				return;
			}
		}
		base.InternalAtmosphere.Sparked = true;
		AtmosphericEventInstance.CloneGlobal(base.WorldGrid, MoleEnergy.Zero, spark: true);
		global::Explosion.Explode(200f, base.ThingTransformLocalPosition, 4f);
		OnServer.Destroy(this);
	}

	public void OnTransmitterCreated()
	{
		Transmitters.AllTransmitters.Add(this);
	}

	public override void BuildUpdate(RocketBinaryWriter writer, ushort networkUpdateType)
	{
		base.BuildUpdate(writer, networkUpdateType);
		if (Thing.IsNetworkUpdateRequired(256u, networkUpdateType))
		{
			writer.WriteSingle(TargetX);
			writer.WriteSingle(TargetY);
			writer.WriteSingle(TargetZ);
		}
	}

	public override void ProcessUpdate(RocketBinaryReader reader, ushort networkUpdateType)
	{
		base.ProcessUpdate(reader, networkUpdateType);
		if (Thing.IsNetworkUpdateRequired(256u, networkUpdateType))
		{
			TargetX = reader.ReadSingle();
			TargetY = reader.ReadSingle();
			TargetZ = reader.ReadSingle();
		}
	}

	public override void SerializeOnJoin(RocketBinaryWriter writer)
	{
		base.SerializeOnJoin(writer);
		writer.WriteSingle(TargetX);
		writer.WriteSingle(TargetY);
		writer.WriteSingle(TargetZ);
	}

	public override void DeserializeOnJoin(RocketBinaryReader reader)
	{
		base.DeserializeOnJoin(reader);
		TargetX = reader.ReadSingle();
		TargetY = reader.ReadSingle();
		TargetZ = reader.ReadSingle();
	}

	public void OnPowerTick()
	{
		if (!GameManager.RunSimulation || IsCursor || GameManager.GameState != GameState.Running)
		{
			return;
		}
		float num = ((CurrentMotorPower > 0f) ? (CurrentMotorPower * 10f) : 0f);
		if (OnOff)
		{
			num += 5f;
		}
		int num2 = 0;
		if (OnOff)
		{
			float num3 = 0f;
			float num4 = 0f;
			BatteryCell batteryCell = BatterySlot.Occupant as BatteryCell;
			if (batteryCell != null)
			{
				num3 += batteryCell.PowerMaximum;
				num4 += batteryCell.PowerStored;
				if (!batteryCell.IsEmpty)
				{
					batteryCell.PowerStored -= num;
					num = 0f;
				}
			}
			if (num <= 0f && num3 > 0f && num4 > 0f)
			{
				float num5 = num4 / num3;
				num2 = ((num5 <= 0.2f) ? 1 : ((num5 <= 0.4f) ? 2 : ((num5 <= 0.6f) ? 3 : ((!(num5 <= 0.8f)) ? 5 : 4))));
			}
		}
		if (PoweredValue != num2)
		{
			OnServer.Interact(base.InteractPowered, num2);
		}
	}

	public void Execute()
	{
		if (GameManager.GameState != GameState.None && !IsCursor && Powered && OnOff && (bool)Battery && !Battery.IsEmpty && GameManager.GameState == GameState.Running && !WorldManager.IsGamePaused && !(ProgrammableChip == null) && !(Battery == null) && !ProgrammableChip.CompilationError)
		{
			Battery.PowerStored -= 2.5f;
			ProgrammableChip.Execute(128);
		}
	}

	public void Recharge(float ammount)
	{
	}

	public override bool CanLogicRead(LogicType logicType)
	{
		switch (logicType)
		{
		case LogicType.PressureExternal:
		case LogicType.TemperatureExternal:
		case LogicType.PositionX:
		case LogicType.PositionY:
		case LogicType.PositionZ:
		case LogicType.VelocityMagnitude:
		case LogicType.VelocityRelativeX:
		case LogicType.VelocityRelativeY:
		case LogicType.VelocityRelativeZ:
		case LogicType.ForwardX:
		case LogicType.ForwardY:
		case LogicType.ForwardZ:
		case LogicType.Orientation:
		case LogicType.VelocityX:
		case LogicType.VelocityY:
		case LogicType.VelocityZ:
			return true;
		default:
			return base.CanLogicRead(logicType);
		}
	}

	public override bool CanLogicWrite(LogicType logicType)
	{
		if (logicType - 88 <= LogicType.Open)
		{
			return true;
		}
		return base.CanLogicWrite(logicType);
	}

	public override double GetLogicValue(LogicType logicType)
	{
		switch (logicType)
		{
		case LogicType.TemperatureExternal:
			if (base.WorldAtmosphere == null)
			{
				return 0.0;
			}
			return base.WorldAtmosphere.Temperature.ToDouble();
		case LogicType.PressureExternal:
			if (base.WorldAtmosphere == null)
			{
				return 0.0;
			}
			return base.WorldAtmosphere.PressureGassesAndLiquids.ToDouble();
		case LogicType.PositionX:
			return base.Position.x;
		case LogicType.PositionY:
			return base.Position.y;
		case LogicType.PositionZ:
			return base.Position.z;
		case LogicType.VelocityMagnitude:
			return base.VelocityMagnitude;
		case LogicType.VelocityRelativeX:
			return RelativeVelocity.x;
		case LogicType.VelocityRelativeY:
			return RelativeVelocity.y;
		case LogicType.VelocityRelativeZ:
			return RelativeVelocity.z;
		case LogicType.VelocityX:
			return base.Velocity.x;
		case LogicType.VelocityY:
			return base.Velocity.y;
		case LogicType.VelocityZ:
			return base.Velocity.z;
		case LogicType.Orientation:
			return Orientation;
		case LogicType.ForwardX:
			return Forward.x;
		case LogicType.ForwardY:
			return Forward.y;
		case LogicType.ForwardZ:
			return Forward.z;
		case LogicType.MineablesInVicinity:
			return VoxelTerrain.GetNumberOfMinablesNearSurface(base.ThingTransformPosition, MinableSearchArea, maxMiningDepth);
		case LogicType.MineablesInQueue:
			return _minableDataQueue.Count;
		default:
			return base.GetLogicValue(logicType);
		}
	}

	public override void SetLogicValue(LogicType logicType, double value)
	{
		switch (logicType)
		{
		case LogicType.TargetX:
			TargetX = (float)value;
			break;
		case LogicType.TargetY:
			TargetY = (float)value;
			break;
		case LogicType.TargetZ:
			TargetZ = (float)value;
			break;
		}
		base.SetLogicValue(logicType, value);
	}

	public override void OnChildEnterInventory(DynamicThing newChild)
	{
		base.OnChildEnterInventory(newChild);
		RefreshError();
		if (GameManager.GameState != GameState.Running)
		{
			return;
		}
		if (ProgrammableChip != null && newChild is ProgrammableChip)
		{
			if (GameManager.RunSimulation)
			{
				OnServer.Interact(base.InteractMode, 0);
			}
			TargetX = 0f;
			TargetY = 0f;
			TargetZ = 0f;
			TargetMinable = null;
			ProgrammableChip.Reset();
		}
		ClearError();
	}

	public override void OnChildExitInventory(DynamicThing previousChild)
	{
		base.OnChildExitInventory(previousChild);
		RefreshError();
	}

	public override void OnInteractableUpdated(Interactable interactable)
	{
		base.OnInteractableUpdated(interactable);
		if (interactable.Action != InteractableType.Error)
		{
			Achievements.AssessAimeeDoesNotUnderstand(interactable, Powered);
			RefreshError();
		}
	}

	public void RefreshError()
	{
		if (GameManager.RunSimulation)
		{
			if (!IsOperable && Error == 0)
			{
				OnServer.Interact(base.InteractError, 1);
			}
			else if (IsOperable && Error == 1)
			{
				OnServer.Interact(base.InteractError, 0);
			}
		}
	}

	public void ClearError()
	{
		RaiseError(0);
		PathList = null;
	}

	public void RaiseError(int state)
	{
		_codeErrorState = state;
		RefreshError();
	}

	public ILogicable GetLogicableFromIndex(int deviceIndex, int networkIndex = int.MinValue)
	{
		if (deviceIndex == int.MaxValue)
		{
			return this;
		}
		return null;
	}

	public ILogicable GetLogicableFromId(int deviceId, int networkIndex = int.MinValue)
	{
		if (deviceId == 0L)
		{
			return null;
		}
		ILogicable logicable = Referencable.Find<ILogicable>(deviceId);
		if (logicable == null)
		{
			return null;
		}
		foreach (Slot slot in Slots)
		{
			if (slot.Occupant == logicable)
			{
				return logicable;
			}
		}
		return null;
	}

	public bool IsValidIndex(int index)
	{
		if (index != int.MaxValue && index != 0)
		{
			return index == 1;
		}
		return true;
	}

	public void SetDeviceLabel(int index, string label)
	{
	}

	public void SetSourceCode(string sourceCode)
	{
		if ((bool)ProgrammableChip)
		{
			ProgrammableChip.SetSourceCode(sourceCode, this);
			ProgrammableChip.SendUpdate();
		}
	}

	public string GetSourceCode()
	{
		if (!ProgrammableChip)
		{
			return "";
		}
		return ProgrammableChip.GetSourceCode();
	}

	protected override void InitialiseSaveData(ref ThingSaveData savedData)
	{
		base.InitialiseSaveData(ref savedData);
		RobotSaveData robotSaveData = savedData as RobotSaveData;
		if (GameManager.GameState != GameState.None && robotSaveData != null)
		{
			robotSaveData.TargetX = TargetX;
			robotSaveData.TargetY = TargetY;
			robotSaveData.TargetZ = TargetZ;
		}
	}

	public override ThingSaveData SerializeSave()
	{
		ThingSaveData savedData = new RobotSaveData();
		InitialiseSaveData(ref savedData);
		return savedData;
	}

	public override void DeserializeSave(ThingSaveData saveData)
	{
		base.DeserializeSave(saveData);
		if (saveData is RobotSaveData robotSaveData)
		{
			TargetX = robotSaveData.TargetX;
			TargetY = robotSaveData.TargetY;
			TargetZ = robotSaveData.TargetZ;
		}
	}

	public override void Awake()
	{
		base.Awake();
		if (GameManager.GameState == GameState.None)
		{
			return;
		}
		foreach (Slot slot2 in Slots)
		{
			_ = slot2;
			_logicList.Add(null);
		}
		ElectricityManager.Register(this);
		AllRobots.Add(this);
		for (int i = 0; i < Slots.Count; i++)
		{
			Slot slot = Slots[i];
			if (slot.HidesOccupant)
			{
				_storageSlots.Add(slot);
			}
		}
		if (GameManager.RunSimulation && ProgrammableChipSlot != null)
		{
			if (NetworkManager.IsServer)
			{
				Tracker.TrackableObjects.Add(this);
			}
			else if (NetworkManager.IsClient)
			{
				NetworkClient.SendToServer(new TrackerMessageFromServer
				{
					ToAdd = true,
					ThingId = base.netId
				});
			}
		}
		foreach (Wheel wheel in Wheels)
		{
			wheel.Parent = this;
		}
	}

	public override DelayedActionInstance InteractWith(Interactable interactable, Interaction interaction, bool doAction = true)
	{
		DelayedActionInstance delayedActionInstance = base.InteractWith(interactable, interaction, doAction);
		ProgrammableChip?.AppendErrorsToActionInstance(delayedActionInstance);
		return delayedActionInstance;
	}

	public override bool MoveToWorld(Vector3 worldPosition, Quaternion worldRotation, Vector3 velocity, Vector3 angularVelocity, float force = 0f)
	{
		bool result = base.MoveToWorld(worldPosition, worldRotation, velocity, angularVelocity, force);
		WheelsT.SetActive(value: true);
		return result;
	}

	public override bool MoveToWorld(float force = 0f)
	{
		bool result = base.MoveToWorld(force);
		WheelsT.SetActive(value: true);
		return result;
	}

	public override void OnEnterInventory(Thing parent)
	{
		base.OnEnterInventory(parent);
		WheelsT.SetActive(value: false);
	}

	private bool MoveTowards(Vector3 worldPosition, float precision = 1f)
	{
		if (!OnOff || !Powered || Error > 0)
		{
			return false;
		}
		Vector3 forward = worldPosition - base.ThingTransformPosition;
		float velocityMagnitude = base.VelocityMagnitude;
		if (forward.magnitude < precision)
		{
			base.TargetMotorPower = 0f;
			base.TargetBrakePower = Power;
			return false;
		}
		Quaternion b = Quaternion.LookRotation(forward);
		float num = Mathf.DeltaAngle(ThingTransform.eulerAngles.y, b.eulerAngles.y);
		if (Mathf.Abs(num) > 45f && velocityMagnitude < 2f)
		{
			RigidBody.MoveRotation(Quaternion.Slerp(ThingTransform.rotation, b, Time.deltaTime * 1f));
			base.TargetBrakePower = Power;
			return true;
		}
		Delta = Mathf.Clamp(num, -35f, 35f);
		base.TargetSteeringAngle = Delta;
		float num2 = Mathf.Clamp(forward.magnitude * 0.5f, 0.1f, 1f);
		float power = Power;
		float max = MaxSpeed * num2;
		base.TargetMotorPower = RocketMath.MapToScale(0f, max, power, 0f, velocityMagnitude);
		base.TargetBrakePower = ((velocityMagnitude > MaxSpeed) ? RocketMath.MapToScale(MaxSpeed, MaxSpeed * 1.3f, 0f, Power, velocityMagnitude) : 0f);
		return true;
	}

    //simple routine to follow nearest human. Can probably be reused.
	private void Follow()
	{
		if (!Target)
		{
			Target = Human.GetNearest(base.ThingTransformPosition);
		}
		MoveTowards(Target.ThingTransformPosition);
	}

	public override void UpdateAudio(float deltaTime)
	{
		base.UpdateAudio(deltaTime);
		if (!WorldManager.IsGamePaused)
		{
			if (!OnOff || !Powered || Error > 0)
			{
				base.TargetBrakePower = (((float)Error > 0f) ? Power : 0f);
				base.TargetMotorPower = 0f;
				EngineSound = false;
			}
			else
			{
				EngineSound = Powered && WheelsTurning();
			}
		}
	}

    //where aimees mode is processed, does so each frame
    //so we would add an entrypoint into our  grab item method here depending on mode.
	public override void UpdateEachFrame()
	{
		base.UpdateEachFrame();
		if (WorldManager.IsGamePaused || !GameManager.RunSimulation)
		{
			return;
		}
		switch ((RobotMode)Mode)
		{
		case RobotMode.Follow:
			TargetMinable = null;
			TryUnstuck();
			Follow();
			break;
		case RobotMode.MoveToTarget:
			TargetMinable = null;
			TryUnstuck();
			MoveToTarget();
			break;
		case RobotMode.Roam:
			TryUnstuck();
			Roam();
			break;
		case RobotMode.Unload:
			TargetMinable = null;
			if (_unloadTimeout > 0f)
			{
				_unloadTimeout -= Time.deltaTime;
			}
			else
			{
				Unload();
			}
			break;
		default:
			base.TargetBrakePower = Power;
			Target = null;
			break;
		}
	}

    //will need total rework for jet audio
    //check jetpack audio
	public void SetEngineAudio()
	{
		float num = Wheels[0].WheelRpm * GearRatio;
		_enginePitch = Mathf.Lerp(_enginePitch, Mathf.Clamp01(num / MaxRpm), Time.deltaTime * LerpSpeed);
		float pitch = Mathf.Lerp(0.5f, 0.75f, _enginePitch);
		float volumeMultiplier = Mathf.Clamp01(num / 1000f);
		GetAudioEvent(EngineAudioHash).SetVolumeAndPitch(volumeMultiplier, pitch);
	}

    //this needs to be "jetsBurning"
	public bool WheelsTurning()
	{
		foreach (Wheel wheel in Wheels)
		{
			if (wheel.WheelRpm > 0.001f)
			{
				return true;
			}
		}
		return false;
	}

    //hovee note: change this to find nearest portable connector
    //add separate method called Drop() which will literally drop whatever is currently held.
	private void Unload()
	{
		IRobotInput nearestRobotInput = Device.GetNearestRobotInput(base.ThingTransformPosition, 3f);
		if (nearestRobotInput != null && !nearestRobotInput.InputSlot.Occupant && nearestRobotInput.AllowInput)
		{
			_lastIsStuckCheckTime = Time.time;
			_unloadTimeout = 3f;
			foreach (Slot storageSlot in _storageSlots)
			{
				if ((bool)storageSlot.Occupant)
				{
					OnServer.MoveToSlot(storageSlot.Occupant, nearestRobotInput.InputSlot);
					return;
				}
			}
		}
		if (IsStorageEmpty)
		{
			OnServer.Interact(base.InteractMode, 0);
		}
		if (nearestRobotInput == null)
		{
			OnServer.Interact(base.InteractMode, 0);
		}
	}

	private void MoveToTarget()
	{
		if (!MoveTowards(TargetPosition))
		{
			OnServer.Interact(base.InteractMode, 0);
		}
	}

	private void Roam()
	{
		if (IsStorageFull)
		{
			OnServer.Interact(base.InteractMode, 6);
			return;
		}
		if (_minableDataQueue.Count <= 0)
		{
			ClearMinableQueue();
			VoxelTerrain.GetAimeeMinableQueue(Transform.position, MinableSearchArea, _minableDataQueue, maxMiningDepth);
		}
		if (_minableDataQueue.Count <= 0)
		{
			return;
		}
		List<TargetMinableData> minableDataQueue = _minableDataQueue;
		TargetMinable = minableDataQueue[minableDataQueue.Count - 1];
		if (MovingToMineable())
		{
			return;
		}
		if (_minableScanTimeout > 0f)
		{
			_minableScanTimeout -= Time.deltaTime;
		}
		Vector3 worldCenterOfMass = RigidBody.worldCenterOfMass;
		if (TargetMinable.HasValue && TargetMinable.Value.Vein.TryMineServer(TargetMinable.Value.Vein.GetMinableWorldPosition(TargetMinable.Value.MinableIndex).FloorToInt(), out var createdOre, position))
		{
			OnMinedOre(createdOre);
			TargetMinable.Value.Vein.RemoveAimeeMinableHash(TargetMinable.Value.MinableIndex);
			_minableDataQueue.RemoveAt(_minableDataQueue.Count - 1);
			TargetMinable = null;
			TargetMinableData? targetMinable = TargetMinable;
			if (!targetMinable.HasValue)
			{
				return;
			}
		}
		float power = Power;
		float velocityMagnitude = base.VelocityMagnitude;
		if (_reversing <= 0f && Physics.Raycast(worldCenterOfMass, ThingTransform.forward, out var _, 0.2f, CursorManager.Instance.TerrainHitMask))
		{
			_reversing = 5f;
		}
		if (_reversing > 0f)
		{
			power = RocketMath.MapToScale(0f, MaxSpeed, power, 0f, velocityMagnitude);
			base.TargetMotorPower = 0f - power;
			_reversing -= Time.deltaTime;
			return;
		}
		if (_roamTimeout <= 0f)
		{
			_roamTimeout = UnityEngine.Random.Range(1f, 3f);
			Delta = UnityEngine.Random.Range(-15f, 15f);
			base.TargetSteeringAngle = Delta;
		}
		_roamTimeout -= Time.deltaTime;
		base.TargetMotorPower = RocketMath.MapToScale(0f, MaxSpeed, power, 0f, velocityMagnitude);
		base.TargetBrakePower = ((velocityMagnitude > MaxSpeed) ? RocketMath.MapToScale(MaxSpeed, MaxSpeed * 1.3f, 0f, Power, velocityMagnitude) : 0f);
	}

	public override void FollowPath(RoomManager.PathfindingTask pathfindingTask)
	{
		if (pathfindingTask.Result == null || pathfindingTask.Result.Count == 0)
		{
			IsBusy = false;
		}
		else
		{
			PathList = pathfindingTask.Result;
		}
	}

	public virtual void OnMinedOre(Ore oreMined)
	{
		foreach (Slot storageSlot in _storageSlots)
		{
			if (!storageSlot.IsAllowedType(oreMined))
			{
				continue;
			}
			if ((bool)storageSlot.Occupant && storageSlot.Occupant.PrefabHash == oreMined.PrefabHash)
			{
				OnServer.Merge(storageSlot.Occupant as Stackable, oreMined);
				OnServer.Interact(base.InteractMode, 0);
				if (oreMined.Quantity <= 0)
				{
					break;
				}
			}
			else
			{
				OnServer.MoveToSlot(oreMined, storageSlot);
				if (IsStorageFull)
				{
                    //seems to be synchronizing aimee mode with server
					OnServer.Interact(base.InteractMode, 6);
				}
				else
				{
					OnServer.Interact(base.InteractMode, 0);
				}
			}
		}
		TargetMinableData? targetMinable = TargetMinable;
		if (targetMinable.HasValue)
		{
			TargetMinable = (TargetMinable.Value.Vein.GetActive(TargetMinable.Value.MinableIndex) ? ((TargetMinableData?)null) : TargetMinable);
		}
	}

	public bool IsAvailable()
	{
		return IsOperable;
	}
}
