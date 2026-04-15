-- PostgreSQL Schema for STS2 Game Knowledge Database
--
-- Note: This schema intentionally uses TEXT for enum-like fields instead of
-- PostgreSQL enum types to keep ingestion/query compatibility simple across
-- extractor and viewer flows.

-- Characters table
CREATE TABLE IF NOT EXISTS characters (
    id TEXT PRIMARY KEY,
    title TEXT,
    description TEXT,
    starting_hp INTEGER,
    starting_gold INTEGER,
    max_energy INTEGER DEFAULT 3,
    orb_slots INTEGER DEFAULT 0,
    starting_deck TEXT[],
    starting_relics TEXT[]
);

-- Cards table
CREATE TABLE IF NOT EXISTS cards (
    id TEXT PRIMARY KEY,
    character_id TEXT,
    title TEXT,
    description TEXT,
    type TEXT,
    rarity TEXT,
    energy_cost INTEGER,
    target TEXT,
    gains_block BOOLEAN,
    keywords TEXT[],
    damage_base INTEGER,
    damage_upgraded INTEGER,
    block_base INTEGER,
    block_upgraded INTEGER,
    magic_base INTEGER,
    magic_upgraded INTEGER,
    max_upgrade INTEGER,
    on_play_source TEXT,
    on_upgrade_source TEXT,
    effect_tags TEXT[],
    effect_description TEXT,
    resources_generated TEXT[],
    resources_consumed TEXT[],
    scaling_type TEXT,
    anti_synergy_tags TEXT[]
);

-- Card variants table
CREATE TABLE IF NOT EXISTS card_variants (
    card_id TEXT NOT NULL REFERENCES cards(id),
    variant_kind TEXT,
    upgrade_level INTEGER,
    source_id TEXT,
    energy_cost INTEGER,
    damage_value INTEGER,
    block_value INTEGER,
    magic_value INTEGER,
    effect_tags TEXT[],
    effect_description TEXT,
    resources_generated TEXT[],
    resources_consumed TEXT[],
    scaling_type TEXT,
    anti_synergy_tags TEXT[],
    PRIMARY KEY (card_id, upgrade_level)
);

-- Card companion actions table
CREATE TABLE IF NOT EXISTS card_companion_actions (
    card_id TEXT NOT NULL REFERENCES cards(id),
    companion_id TEXT NOT NULL,
    action_tag TEXT,
    source_method TEXT,
    PRIMARY KEY (card_id, companion_id)
);

-- Relics table
CREATE TABLE IF NOT EXISTS relics (
    id TEXT PRIMARY KEY,
    character_id TEXT,
    title TEXT,
    description TEXT,
    flavor_text TEXT,
    rarity TEXT,
    counter_base INTEGER,
    amount_base INTEGER,
    hook_source TEXT,
    effect_tags TEXT[],
    effect_description TEXT,
    triggers_on TEXT[],
    trigger_condition TEXT,
    resources_generated TEXT[],
    resources_consumed TEXT[]
);

-- Potions table
CREATE TABLE IF NOT EXISTS potions (
    id TEXT PRIMARY KEY,
    character_id TEXT,
    title TEXT,
    description TEXT,
    rarity TEXT,
    amount_base INTEGER,
    on_use_source TEXT,
    effect_tags TEXT[],
    effect_description TEXT
);

-- Powers table
CREATE TABLE IF NOT EXISTS powers (
    id TEXT PRIMARY KEY,
    title TEXT,
    description TEXT,
    type TEXT,
    stack_type TEXT,
    is_player_power BOOLEAN,
    hook_source TEXT,
    effect_tags TEXT[],
    effect_description TEXT,
    triggers_on TEXT[],
    scaling_type TEXT
);

-- Hook listeners table (for cards, relics, powers)
CREATE TABLE IF NOT EXISTS hook_listeners (
    entity_type TEXT NOT NULL,
    entity_id TEXT NOT NULL,
    hook_name TEXT NOT NULL,
    PRIMARY KEY (entity_type, entity_id, hook_name)
);

-- Events table
CREATE TABLE IF NOT EXISTS events (
    id TEXT PRIMARY KEY,
    title TEXT,
    description TEXT,
    is_deterministic BOOLEAN,
    options_summary TEXT
);

-- Event options table
CREATE TABLE IF NOT EXISTS event_options (
    event_id TEXT NOT NULL REFERENCES events(id),
    option_key TEXT NOT NULL,
    title TEXT,
    description TEXT,
    handler_method TEXT,
    handler_source TEXT,
    PRIMARY KEY (event_id, option_key)
);

-- Event outcomes table
CREATE TABLE IF NOT EXISTS event_outcomes (
    event_id TEXT NOT NULL REFERENCES events(id),
    outcome_id TEXT NOT NULL,
    title TEXT,
    description TEXT,
    outcome_kind TEXT,
    outcome_source TEXT,
    PRIMARY KEY (event_id, outcome_id)
);

-- Effect taxonomy table
CREATE TABLE IF NOT EXISTS effect_taxonomy (
    tag TEXT PRIMARY KEY,
    category TEXT,
    description TEXT
);

-- Archetypes table
CREATE TABLE IF NOT EXISTS archetypes (
    id SERIAL PRIMARY KEY,
    character_id TEXT,
    name TEXT NOT NULL,
    description TEXT,
    key_effect_tags TEXT[]
);

CREATE TABLE IF NOT EXISTS entity_archetype_affinity (
    archetype_id INTEGER NOT NULL REFERENCES archetypes(id),
    entity_type TEXT NOT NULL,
    entity_id TEXT NOT NULL,
    affinity_score INTEGER NOT NULL CHECK (affinity_score BETWEEN 0 AND 10),
    PRIMARY KEY (archetype_id, entity_type, entity_id)
);

-- Synergy clusters table
CREATE TABLE IF NOT EXISTS synergy_clusters (
    id SERIAL PRIMARY KEY,
    archetype_id INTEGER,
    entity_type TEXT,
    entity_id TEXT,
    card_variant_id INTEGER,
    synergy_role TEXT,
    synergy_score DOUBLE PRECISION,
    explanation TEXT
);

-- Entity synergy edges table
CREATE TABLE IF NOT EXISTS entity_synergy_edges (
    id SERIAL PRIMARY KEY,
    entity_a_type TEXT NOT NULL,
    entity_a_id TEXT NOT NULL,
    entity_b_type TEXT NOT NULL,
    entity_b_id TEXT NOT NULL,
    synergy_strength INTEGER NOT NULL,
    is_anti_synergy BOOLEAN DEFAULT FALSE,
    shared_tags TEXT[],
    explanation TEXT,
    UNIQUE (entity_a_type, entity_a_id, entity_b_type, entity_b_id, is_anti_synergy)
);

-- Entity strength ratings table
CREATE TABLE IF NOT EXISTS entity_strength_ratings (
    entity_type TEXT NOT NULL,
    entity_id TEXT NOT NULL,
    card_variant_id INTEGER,
    synergy_rating INTEGER,
    flexibility_rating INTEGER,
    anti_synergy_rating INTEGER,
    rationale TEXT,
    PRIMARY KEY (entity_type, entity_id)
);

-- Run state snapshots
CREATE TABLE IF NOT EXISTS run_states (
    id SERIAL PRIMARY KEY,
    name TEXT NOT NULL,
    character_id TEXT,
    archetype_id INTEGER,
    card_ids TEXT[],
    relic_ids TEXT[],
    created_at TIMESTAMPTZ DEFAULT now()
);

-- Pick advice cache
CREATE TABLE IF NOT EXISTS pick_advice (
    id SERIAL PRIMARY KEY,
    run_state_id INTEGER,
    option_entity_type TEXT NOT NULL,
    option_entity_id TEXT NOT NULL,
    rank INTEGER NOT NULL,
    edge_overlap_score DOUBLE PRECISION,
    archetype_fit_score DOUBLE PRECISION,
    anti_synergy_penalty DOUBLE PRECISION,
    composite_score DOUBLE PRECISION,
    explanation TEXT,
    created_at TIMESTAMPTZ DEFAULT now()
);

-- Create indexes for common queries
CREATE INDEX IF NOT EXISTS idx_cards_character_id ON cards(character_id);
CREATE INDEX IF NOT EXISTS idx_cards_type ON cards(type);
CREATE INDEX IF NOT EXISTS idx_cards_rarity ON cards(rarity);
CREATE INDEX IF NOT EXISTS idx_card_variants_card_id ON card_variants(card_id);
CREATE INDEX IF NOT EXISTS idx_relics_character_id ON relics(character_id);
CREATE INDEX IF NOT EXISTS idx_potions_character_id ON potions(character_id);
CREATE INDEX IF NOT EXISTS idx_powers_stack_type ON powers(stack_type);
CREATE INDEX IF NOT EXISTS idx_hook_listeners_entity ON hook_listeners(entity_type, entity_id);
CREATE INDEX IF NOT EXISTS idx_event_options_event_id ON event_options(event_id);
CREATE INDEX IF NOT EXISTS idx_event_outcomes_event_id ON event_outcomes(event_id);
CREATE INDEX IF NOT EXISTS idx_archetypes_character_id ON archetypes(character_id);
CREATE INDEX IF NOT EXISTS idx_entity_archetype_affinity_entity ON entity_archetype_affinity(entity_type, entity_id);
CREATE INDEX IF NOT EXISTS idx_entity_archetype_affinity_archetype ON entity_archetype_affinity(archetype_id);
CREATE INDEX IF NOT EXISTS idx_synergy_clusters_archetype ON synergy_clusters(archetype_id);
CREATE INDEX IF NOT EXISTS idx_synergy_clusters_entity ON synergy_clusters(entity_type, entity_id);
CREATE INDEX IF NOT EXISTS idx_entity_edges_a ON entity_synergy_edges(entity_a_type, entity_a_id);
CREATE INDEX IF NOT EXISTS idx_entity_edges_b ON entity_synergy_edges(entity_b_type, entity_b_id);
CREATE INDEX IF NOT EXISTS idx_entity_ratings_type ON entity_strength_ratings(entity_type);
CREATE INDEX IF NOT EXISTS idx_run_states_name ON run_states(name);
CREATE INDEX IF NOT EXISTS idx_pick_advice_run_state ON pick_advice(run_state_id);
