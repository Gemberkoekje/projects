package dev.gemberkoekje.skyseed.compat;

import com.mojang.serialization.Codec;

/**
 * A namespaced id as written in Skyseed's data, kept as a raw string — the <b>version-agnostic stand-in for
 * Minecraft's id type</b> (<code>ResourceLocation</code> in 1.21.1, renamed <code>Identifier</code> in 26.1.2). The
 * codec records and the generator carry {@code Id} and never the Minecraft type, so that rename lands only in
 * {@link Ids} / {@link Lookup} (the one place an {@code Id} is turned into the real id). Parsing/validation is deferred
 * to resolution time (see {@link Lookup}), which also lets a build tolerate an id its Minecraft version doesn't know —
 * it simply resolves to nothing (REFACTORPLAN §2.4). See {@code REFACTORPLAN.md} §2.6.
 */
public record Id(String value) {
    /** Decodes the raw id string (no format check here — resolved + validated at use-time via {@link Lookup}). */
    public static final Codec<Id> CODEC = Codec.STRING.xmap(Id::new, Id::value);

    /** An id from its raw {@code namespace:path} string. */
    public static Id of(String value) {
        return new Id(value);
    }

    @Override
    public String toString() {
        return value;
    }
}
