using Google.FlatBuffers;

namespace AUnrealFeatures.Virtex;


public static class CsFramesChunkBuilder
{
    // field-for-field:
    //   slot 4  (field 0): id   - int64  (SteamID64-shaped, read as QWORD)
    //   slot 6  (field 1): team - byte   (ECSTeam enum, default Alpha=0)
    //   slot 8  (field 2): name - string (UTF8, converted via FUTF8ToTCHAR_Convert)
    private static int BuildPlayerInfo(FlatBufferBuilder fbb, long id, string name, byte team)
    {
        var nameOff = fbb.CreateString(name ?? "");
        fbb.StartTable(3);
        fbb.AddLong(0, id, 0);
        fbb.AddByte(1, team, 0);
        fbb.AddOffset(2, nameOff.Value, 0);
        return fbb.EndTable();
    }

    // Cs::Economy (name inferred from FCSPlayersFrameInfo.Economy field it
    //   slot 4 (field 0): money            - ushort
    //   slot 6 (field 1): equipment_value  - ushort
    private static int BuildEconomy(FlatBufferBuilder fbb, ushort money, ushort equipmentValue)
    {
        fbb.StartTable(2);
        fbb.AddUshort(0, money, 0);
        fbb.AddUshort(1, equipmentValue, 0);
        return fbb.EndTable();
    }

    //   slot 4 (field 0): kills   - ushort
    //   slot 6 (field 1): deaths  - ushort
    //   slot 8 (field 2): assists - ushort
    private static int BuildStats(FlatBufferBuilder fbb, ushort kills, ushort deaths, ushort assists)
    {
        fbb.StartTable(3);
        fbb.AddUshort(0, kills, 0);
        fbb.AddUshort(1, deaths, 0);
        fbb.AddUshort(2, assists, 0);
        return fbb.EndTable();
    }

    //   slot 4  (field 0): player_info - table(PlayerInfo)
    //   slot 6  (field 1): team_side   - byte (ECSTeamSide, default Terrorist)
    //   slot 8  (field 2): economy     - table(Economy)
    //   slot 10 (field 3): stats       - table(Stats)
    private static int BuildPlayerFrameInfo(FlatBufferBuilder fbb, long playerId, string playerName, byte team,
        byte teamSide, ushort money, ushort equipmentValue, ushort kills, ushort deaths, ushort assists)
    {
        var playerInfo = BuildPlayerInfo(fbb, playerId, playerName, team);
        var economy = BuildEconomy(fbb, money, equipmentValue);
        var stats = BuildStats(fbb, kills, deaths, assists);
        fbb.StartTable(4);
        fbb.AddOffset(0, playerInfo, 0);
        fbb.AddByte(1, teamSide, 0);
        fbb.AddOffset(2, economy, 0);
        fbb.AddOffset(3, stats, 0);
        return fbb.EndTable();
    }

    // via Cs::WorldItem::rotation()/Cs::Grenade::origin() both returning
    // const Common::Vec3*, and directly read inline (no extra offset
    // Origin/Velocity handling -- 3 floats, x/y/z, 4-byte aligned, 12 bytes.
    private static int BuildVec3(FlatBufferBuilder fbb, float x, float y, float z)
    {
        fbb.Prep(4, 12);
        fbb.PutFloat(z);
        fbb.PutFloat(y);
        fbb.PutFloat(x);
        return fbb.Offset;
    }

    // Cs::AlivePlayerFrameInfo::eye_angles() (real accessor name) mapping to
    // FCSAlivePlayer.EyeAngle.Pitch/.Yaw in GetAlivePlayerFrameInfo -- 2
    // floats, pitch/yaw, 4-byte aligned, 8 bytes.
    private static int BuildVecAngle(FlatBufferBuilder fbb, float pitch, float yaw)
    {
        fbb.Prep(4, 8);
        fbb.PutFloat(yaw);
        fbb.PutFloat(pitch);
        return fbb.Offset;
    }

    // Cs::EquippedWeapon -- fields read directly in
    //   slot 4  (field 0): entity_handle - int   (4 bytes, read as DWORD)
    //   slot 6  (field 1): weapon_type   - byte  (ECSItemType enum)
    //   slot 8  (field 2): clip_ammo     - byte
    //   slot 10 (field 3): reserve_ammo  - ushort
    private static int BuildEquippedWeapon(FlatBufferBuilder fbb, int entityHandle, byte weaponType, byte clipAmmo, ushort reserveAmmo)
    {
        fbb.StartTable(4);
        fbb.AddInt(0, entityHandle, 0);
        fbb.AddByte(1, weaponType, 0);
        fbb.AddByte(2, clipAmmo, 0);
        fbb.AddUshort(3, reserveAmmo, 0);
        return fbb.EndTable();
    }

    /// <summary>A single alive player's per-frame state -- position, health, weapons, etc.
    /// This is what actually makes replay show players moving; everything else in this
    /// file exists to satisfy required nesting so the buffer verifies.</summary>
    public sealed class AlivePlayerSpec
    {
        public long PlayerId;
        public string PlayerName = "";
        public byte Team; // ECSTeam: Alpha=0
        public byte TeamSide; // ECSTeamSide: Terrorist=0 (default)
        public float X, Y, Z;
        public float VelX, VelY, VelZ;
        public float EyePitch, EyeYaw;
        public ushort Health = 100;
        public ushort Armor;
        public byte Fov = 90;
        public byte ActiveWeapon;
    }

    // every field by exact vtable slot -- this is the single most-verified
    // table in this whole schema:
    //   slot 4  (field 0):  player_frame_info - table(PlayerFrameInfo)
    //   slot 6  (field 1):  origin            - struct(Vec3)   [POSITION]
    //   slot 8  (field 2):  velocity          - struct(Vec3)
    //   slot 12 (field 4):  flash_timer       - float
    //   slot 14 (field 5):  flash_max_alpha   - float
    //   slot 16 (field 6):  health            - ushort
    //   slot 18 (field 7):  armor             - ushort
    //   slot 20 (field 8):  fov               - byte
    //   slot 22 (field 9):  active_weapon     - byte
    //   slot 24 (field 10): weapons           - vector<table(EquippedWeapon)>
    //   slot 26 (field 11): is_ducking        - bool
    //   slot 28 (field 12): is_ducked         - bool
    //   slot 30 (field 13): is_scoped         - bool
    //   slot 32 (field 14): in_buy_zone       - bool
    //   slot 34 (field 15): is_defusing       - bool
    //   slot 36 (field 16): is_planting       - bool
    //   slot 38 (field 17): has_helmet        - bool
    //   slot 40 (field 18): has_defuser       - bool
    //   slot 42 (field 19): is_reloading      - bool
    //   slot 44 (field 20): aimpunch_angle    - struct(VecAngle)
    //   slot 46 (field 21): view_punch_angle  - struct(VecAngle)
    private static int BuildAlivePlayerFrameInfo(FlatBufferBuilder fbb, AlivePlayerSpec p)
    {
        var playerFrameInfo = BuildPlayerFrameInfo(fbb, p.PlayerId, p.PlayerName, p.Team, p.TeamSide,
            money: 800, equipmentValue: 0, kills: 0, deaths: 0, assists: 0);

        // Empty weapons vector -- no EquippedWeapon entries needed for a
        // basic position-replay test.
        fbb.StartVector(4, 0, 4);
        var weaponsVec = fbb.EndVector();

        // FlatBuffers requires each struct to be built (Prep+Put) and passed
        // to AddStruct IMMEDIATELY -- "struct must be serialized inline" --
        // no other builder writes (including building another struct) may
        // happen between a struct's construction and its AddStruct call. So
        // each struct is built directly inline with its Add call below,
        // rather than batched up front.
        fbb.StartTable(22);
        fbb.AddOffset(0, playerFrameInfo, 0);
        fbb.AddStruct(1, BuildVec3(fbb, p.X, p.Y, p.Z), 0);
        fbb.AddStruct(2, BuildVec3(fbb, p.VelX, p.VelY, p.VelZ), 0);
        fbb.AddStruct(3, BuildVecAngle(fbb, p.EyePitch, p.EyeYaw), 0);
        fbb.AddFloat(4, 0f, 0); // flash_timer
        fbb.AddFloat(5, 0f, 0); // flash_max_alpha
        fbb.AddUshort(6, p.Health, 0);
        fbb.AddUshort(7, p.Armor, 0);
        fbb.AddByte(8, p.Fov, 0);
        fbb.AddByte(9, p.ActiveWeapon, 0);
        fbb.AddOffset(10, weaponsVec.Value, 0);
        fbb.AddBool(11, false, false); // is_ducking
        fbb.AddBool(12, false, false); // is_ducked
        fbb.AddBool(13, false, false); // is_scoped
        fbb.AddBool(14, false, false); // in_buy_zone
        fbb.AddBool(15, false, false); // is_defusing
        fbb.AddBool(16, false, false); // is_planting
        fbb.AddBool(17, false, false); // has_helmet
        fbb.AddBool(18, false, false); // has_defuser
        fbb.AddBool(19, false, false); // is_reloading
        fbb.AddStruct(20, BuildVecAngle(fbb, 0, 0), 0);
        fbb.AddStruct(21, BuildVecAngle(fbb, 0, 0), 0);
        return fbb.EndTable();
    }

    // (plain VerifyOffset, never VerifyOffsetRequired), so an empty TeamInfo
    // ({} with zero fields set) passes verification cleanly:
    //   slot 4  (field 0): unnamed nested table w/ 1 required string (optional at TeamInfo level -- omit)
    //   slot 6  (field 1): unnamed offset field (optional -- omit)
    //   slot 12 (field 4): coaches(?) - vector<table(PlayerInfo)>-shaped, same pattern as players (optional; field 3/slot 10 unused/deprecated)
    private static int BuildTeamInfo(FlatBufferBuilder fbb, IReadOnlyList<AlivePlayerSpec> players)
    {
        var playerOffsets = new int[players.Count];
        for (int i = 0; i < players.Count; i++)
            playerOffsets[i] = BuildPlayerInfo(fbb, players[i].PlayerId, players[i].PlayerName, players[i].Team);

        fbb.StartVector(4, playerOffsets.Length, 4);
        for (int i = playerOffsets.Length - 1; i >= 0; i--)
            fbb.AddOffset(playerOffsets[i]);
        var playersVec = fbb.EndVector();

        fbb.StartTable(5); // fields 0..4 (field 3 / slot 0xA left unused)
        fbb.AddOffset(2, playersVec.Value, 0);
        return fbb.EndTable();
    }

    // RoundContext's two Score-typed fields) but no Verify()/accessor body
    // was located this pass -- built as a bare empty table. flatbuffers
    // tables with zero fields set always pass VerifyTableStart trivially, so
    // this is safe regardless of Score's real (unknown) field layout, AS
    // there's no evidence otherwise and it's consistent with every other
    // "small stat struct" in this schema (Economy/Stats) using ushort scalars
    // with implicit defaults, not required fields.
    private static int BuildEmptyScore(FlatBufferBuilder fbb)
    {
        fbb.StartTable(0);
        return fbb.EndTable();
    }

    //   slot 4  (field 0): match_context - table(MatchContext), OPTIONAL -- omit
    //                      FChunkInfo.MapId -- NOT sourced from the MatchChunk JSON
    //                      envelope's MapInfo field at all, despite that field existing.
    //                      calls Get Map Enum(ReadFrameChunk.MapId) and Load Map(...) if it
    //                      differs from the current map -- this MUST be a real CS map name
    //   slot 8  (field 2): score (alpha?) - table(Score), REQUIRED
    //   slot 10 (field 3): score (bravo?) - table(Score), REQUIRED
    //   slot 12 (field 4): byte, REQUIRED (VerifyField<byte>)
    //   slot 14 (field 5): byte, REQUIRED
    //   slot 16 (field 6): byte, REQUIRED
    //   slot 18 (field 7): byte, REQUIRED
    //   slot 20 (field 8): alpha_team_info - table(TeamInfo), REQUIRED
    //   slot 24 (field 10): byte, REQUIRED
    private static int BuildRoundContext(FlatBufferBuilder fbb, string mapId,
        IReadOnlyList<AlivePlayerSpec> alphaPlayers, IReadOnlyList<AlivePlayerSpec> bravoPlayers)
    {
        var mapIdOffset = fbb.CreateString(mapId);
        var scoreAlpha = BuildEmptyScore(fbb);
        var scoreBravo = BuildEmptyScore(fbb);
        var teamAlpha = BuildTeamInfo(fbb, alphaPlayers);
        var teamBravo = BuildTeamInfo(fbb, bravoPlayers);

        fbb.StartTable(11);
        fbb.AddOffset(1, mapIdOffset.Value, 0);
        fbb.AddOffset(2, scoreAlpha, 0);
        fbb.AddOffset(3, scoreBravo, 0);
        fbb.AddByte(4, 0, 0);
        fbb.AddByte(5, 0, 0);
        fbb.AddByte(6, 0, 0);
        fbb.AddByte(7, 0, 0);
        fbb.AddOffset(8, teamAlpha, 0);
        fbb.AddOffset(9, teamBravo, 0);
        fbb.AddByte(10, 0, 0);
        return fbb.EndTable();
    }

    /// <summary>Builds one Cs::Frame with the given alive players (split alpha/bravo by
    /// AlivePlayerSpec.Team) and every other REQUIRED vector left empty (dead_players,
    /// grenades, infernos, smokes, items, props, events, sounds). Optional fields
    // that ties everything else together -- see inline slot comments there):
    //   slot 10 (field 3):  [scalar] float, optional (VerifyField, has implicit default)
    //   slot 12 (field 4):  countdown      - table(Countdown), OPTIONAL -- omit
    //   slot 14 (field 5):  round_context  - table(RoundContext), REQUIRED
    //   slot 16 (field 6):  [alive players] vector<table(AlivePlayerFrameInfo)>, REQUIRED
    //                       ICF folding -- name is WRONG, but each element IS verified via
    //                       AlivePlayerFrameInfo::Verify, confirming this is the alive-player list)
    //   slot 22 (field 9):  [infernos]     vector<table(Inferno)>, REQUIRED (accessor
    //                       each element verified via Inferno::Verify)
    //                       as Round::winner_team, ICF-folded/WRONG name; verified via Smoke::Verify)
    //                       as Round::bravo_final_score, ICF-folded/WRONG name; verified via Prop::Verify)
    //   slot 34 (field 15): bomb           - table(BombPlantFrame), OPTIONAL (plain VerifyOffset) -- omit
    //   slot 36 (field 16): slack_time_info- table(SlackTimeInfo), OPTIONAL -- omit
    private static int BuildFrame(FlatBufferBuilder fbb, string mapId, IReadOnlyList<AlivePlayerSpec> alivePlayers)
    {
        var alphaPlayers = new List<AlivePlayerSpec>();
        var bravoPlayers = new List<AlivePlayerSpec>();
        foreach (var p in alivePlayers)
            (p.Team == 0 ? alphaPlayers : bravoPlayers).Add(p);

        var roundContext = BuildRoundContext(fbb, mapId, alphaPlayers, bravoPlayers);

        var aliveOffsets = new int[alivePlayers.Count];
        for (int i = 0; i < alivePlayers.Count; i++)
            aliveOffsets[i] = BuildAlivePlayerFrameInfo(fbb, alivePlayers[i]);
        fbb.StartVector(4, aliveOffsets.Length, 4);
        for (int i = aliveOffsets.Length - 1; i >= 0; i--)
            fbb.AddOffset(aliveOffsets[i]);
        var aliveVec = fbb.EndVector();

        // All other REQUIRED vectors: empty (0 elements) -- structurally
        // valid, just no dead players/grenades/infernos/smokes/props/events/
        // sounds this frame.
        int EmptyVec() { fbb.StartVector(4, 0, 4); return fbb.EndVector().Value; }
        var deadPlayersVec = EmptyVec();
        var grenadesVec = EmptyVec();
        var infernosVec = EmptyVec();
        var smokesVec = EmptyVec();
        var itemsVec = EmptyVec();
        var propsVec = EmptyVec();
        var eventsVec = EmptyVec();
        var soundsVec = EmptyVec();

        fbb.StartTable(18);
        fbb.AddFloat(3, 0f, 0);
        fbb.AddOffset(5, roundContext, 0);
        fbb.AddOffset(6, aliveVec.Value, 0);
        fbb.AddOffset(7, deadPlayersVec, 0);
        fbb.AddOffset(8, grenadesVec, 0);
        fbb.AddOffset(9, infernosVec, 0);
        fbb.AddOffset(10, smokesVec, 0);
        fbb.AddOffset(11, itemsVec, 0);
        fbb.AddOffset(12, propsVec, 0);
        fbb.AddOffset(13, eventsVec, 0);
        fbb.AddOffset(14, soundsVec, 0);
        return fbb.EndTable();
    }

    /// <summary>10 alive players (5 Alpha/CT, 5 Bravo/T) in a loose line at opposite
    /// the identical mock data.</summary>
    public static List<AlivePlayerSpec> BuildMockAlivePlayers()
    {
        var alivePlayers = new List<AlivePlayerSpec>();
        for (int i = 0; i < 5; i++)
        {
            alivePlayers.Add(new AlivePlayerSpec
            {
                PlayerId = 76561198000000000L + i,
                PlayerName = $"Alpha_{i + 1}",
                Team = 0, // Alpha
                TeamSide = 1, // CounterTerrorist
                X = -200f + i * 60f, Y = -400f, Z = 0f,
                EyeYaw = 90f,
            });
            alivePlayers.Add(new AlivePlayerSpec
            {
                PlayerId = 76561198100000000L + i,
                PlayerName = $"Bravo_{i + 1}",
                Team = 1, // Bravo
                TeamSide = 0, // Terrorist
                X = -200f + i * 60f, Y = 400f, Z = 0f,
                EyeYaw = -90f,
            });
        }
        return alivePlayers;
    }

    public static byte[] BuildMockFramesChunk(string mapId, IReadOnlyList<AlivePlayerSpec> alivePlayers, int frameCount = 1)
    {
        var fbb = new FlatBufferBuilder(4096);

        var frameOffsets = new int[frameCount];
        for (int i = 0; i < frameCount; i++)
            frameOffsets[i] = BuildFrame(fbb, mapId, alivePlayers);

        fbb.StartVector(4, frameOffsets.Length, 4);
        for (int i = frameOffsets.Length - 1; i >= 0; i--)
            fbb.AddOffset(frameOffsets[i]);
        var framesVec = fbb.EndVector();

        fbb.StartTable(1);
        fbb.AddOffset(0, framesVec.Value, 0);
        var root = fbb.EndTable();

        fbb.Finish(root);
        return fbb.SizedByteArray();
    }
}
