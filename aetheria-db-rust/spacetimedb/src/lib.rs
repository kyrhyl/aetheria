use spacetimedb::{Identity, ReducerContext, Table, Timestamp};

#[spacetimedb::table(accessor = person, public)]
pub struct Person {
    name: String,
}

#[spacetimedb::table(accessor = player_profile, public)]
pub struct PlayerProfile {
    #[primary_key]
    identity: Identity,
    username: String,
    x: f32,
    y: f32,
    z: f32,
    last_seen: Timestamp,
}

#[spacetimedb::table(accessor = player_presence, public, index(name = "player_presence_by_identity", accessor = player_presence_by_identity, btree(columns = [identity])))]
pub struct PlayerPresence {
    #[primary_key]
    profile: String,
    identity: Identity,
    online: bool,
    x: f32,
    y: f32,
    z: f32,
    last_seen: Timestamp,
}

#[spacetimedb::reducer(init)]
pub fn init(_ctx: &ReducerContext) {}

#[spacetimedb::reducer(client_connected)]
pub fn identity_connected(ctx: &ReducerContext) {
    if let Some(profile) = ctx.db.player_profile().identity().find(ctx.sender()) {
        ctx.db.player_profile().identity().update(PlayerProfile {
            last_seen: ctx.timestamp,
            ..profile
        });
    } else {
        ctx.db.player_profile().insert(PlayerProfile {
            identity: ctx.sender(),
            username: String::new(),
            x: 24.0,
            y: 8.0,
            z: 24.0,
            last_seen: ctx.timestamp,
        });
    }
}

#[spacetimedb::reducer(client_disconnected)]
pub fn identity_disconnected(ctx: &ReducerContext) {
    if let Some(profile) = ctx.db.player_profile().identity().find(ctx.sender()) {
        ctx.db.player_profile().identity().update(PlayerProfile {
            last_seen: ctx.timestamp,
            ..profile
        });
    }

    let mut stale: Vec<PlayerPresence> = Vec::new();
    for presence in ctx.db.player_presence().iter() {
        if presence.identity == ctx.sender() && presence.online {
            stale.push(presence);
        }
    }

    for presence in stale {
        ctx.db.player_presence().profile().update(PlayerPresence {
            online: false,
            last_seen: ctx.timestamp,
            ..presence
        });
    }
}

#[spacetimedb::reducer]
pub fn add(ctx: &ReducerContext, name: String) {
    ctx.db.person().insert(Person { name });
}

#[spacetimedb::reducer]
pub fn say_hello(ctx: &ReducerContext) {
    for person in ctx.db.person().iter() {
        log::info!("Hello, {}!", person.name);
    }
    log::info!("Hello, World!");
}

#[spacetimedb::reducer]
pub fn upsert_player_spawn(ctx: &ReducerContext, x: f32, y: f32, z: f32) {
    save_player_position(ctx, x, y, z);
}

#[spacetimedb::reducer]
pub fn save_player_position(ctx: &ReducerContext, x: f32, y: f32, z: f32) {
    if let Some(profile) = ctx.db.player_profile().identity().find(ctx.sender()) {
        ctx.db.player_profile().identity().update(PlayerProfile {
            x,
            y,
            z,
            last_seen: ctx.timestamp,
            ..profile
        });
    } else {
        ctx.db.player_profile().insert(PlayerProfile {
            identity: ctx.sender(),
            username: String::new(),
            x,
            y,
            z,
            last_seen: ctx.timestamp,
        });
    }
}

#[spacetimedb::reducer]
pub fn set_player_profile(ctx: &ReducerContext, profile: String) {
    let profile_value = profile.trim().to_string();
    if profile_value.is_empty() {
        return;
    }

    let (x, y, z) = if let Some(saved) = ctx.db.player_profile().identity().find(ctx.sender()) {
        (saved.x, saved.y, saved.z)
    } else {
        (24.0, 8.0, 24.0)
    };

    if let Some(existing) = ctx.db.player_presence().profile().find(&profile_value) {
        ctx.db.player_presence().profile().update(PlayerPresence {
            identity: ctx.sender(),
            online: true,
            x,
            y,
            z,
            last_seen: ctx.timestamp,
            ..existing
        });
        return;
    }

    ctx.db.player_presence().insert(PlayerPresence {
        profile: profile_value,
        identity: ctx.sender(),
        online: true,
        x,
        y,
        z,
        last_seen: ctx.timestamp,
    });
}

#[spacetimedb::reducer]
pub fn set_player_username(ctx: &ReducerContext, username: String) {
    let username_value = username.trim().to_string();
    if username_value.is_empty() {
        return;
    }

    if let Some(saved) = ctx.db.player_profile().identity().find(ctx.sender()) {
        ctx.db.player_profile().identity().update(PlayerProfile {
            username: username_value,
            last_seen: ctx.timestamp,
            ..saved
        });
    } else {
        ctx.db.player_profile().insert(PlayerProfile {
            identity: ctx.sender(),
            username: username_value,
            x: 24.0,
            y: 8.0,
            z: 24.0,
            last_seen: ctx.timestamp,
        });
    }
}

#[spacetimedb::reducer]
pub fn save_player_position_for_profile(ctx: &ReducerContext, profile: String, x: f32, y: f32, z: f32) {
    let profile_value = profile.trim().to_string();
    if profile_value.is_empty() {
        return;
    }

    save_player_position(ctx, x, y, z);

    if let Some(existing) = ctx.db.player_presence().profile().find(&profile_value) {
        ctx.db.player_presence().profile().update(PlayerPresence {
            identity: ctx.sender(),
            online: true,
            x,
            y,
            z,
            last_seen: ctx.timestamp,
            ..existing
        });
        return;
    }

    ctx.db.player_presence().insert(PlayerPresence {
        profile: profile_value,
        identity: ctx.sender(),
        online: true,
        x,
        y,
        z,
        last_seen: ctx.timestamp,
    });
}
