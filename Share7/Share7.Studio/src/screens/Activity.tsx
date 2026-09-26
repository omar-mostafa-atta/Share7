import { useState } from "react";
import { useLedge } from "../App";
import { Nothing, Wiping } from "../board/pieces";
import { useI18n } from "../i18n/i18n";
import type { MessageKey } from "../i18n/en";
import { studio, type ActivityItem } from "../lib/studio";
import { useLoad } from "../lib/use";

// ===========================================================================
// Activity — who changed what
//
// A read of the same permanent record the SuperAdmin's audit log reads, cut to
// this team's work. Nothing in the Studio happens without a line here, and no
// line here can be edited or removed: it is written in the same transaction as
// the change it describes.
// ===========================================================================

type Say = ReturnType<typeof useI18n>["t"];

/**
 * What kind of thing happened, in words. The feed is read by the team, so an
 * action this build has never heard of says nothing at all rather than showing
 * the event's key — the key belongs in the audit log, not on the board.
 */
const named = (t: Say, action: string) => {
  const key = `action.${action}` as MessageKey;
  const said = t(key);
  return said === key ? "" : said;
};

/** The seven kinds of draft, as the record spells them. */
const kinds = [
  "LessonContent",
  "NewNode",
  "Rename",
  "Move",
  "Reorder",
  "Retire",
  "Restore",
] as const;

/** …and as the server writes them now that it writes them in words. */
const inWords: Record<string, (typeof kinds)[number]> = {
  "a lesson's questions": "LessonContent",
  "something new": "NewNode",
  "a new name": "Rename",
  "a move": "Move",
  "a new order": "Reorder",
  "taking something out": "Retire",
  "putting something back": "Restore",
};

/**
 * The permanent record is written in English, once, and is never rewritten —
 * including the rows written before the server learned to name a draft's kind
 * in words rather than as `LessonContent`. Both forms are read back into the
 * member's own language here, on the way to the board. The archive keeps
 * exactly what it said; nobody has to read an enum to find out what they did
 * yesterday, and an Arabic Studio does not show half its feed in English.
 */
const saying = (t: Say, summary: string) =>
  summary.replace(
    /^Started a draft: (.+)\.$/,
    (whole: string, said: string) => {
      const plain = said.replace(/[\u2018\u2019]/g, "'").toLowerCase();
      const kind =
        inWords[plain] ?? kinds.find((one) => one.toLowerCase() === plain);
      if (!kind) return whole;
      const words = t(`kind.${kind}` as MessageKey);
      return t("activity.startedDraft", {
        kind: words.charAt(0).toLocaleLowerCase() + words.slice(1),
      });
    },
  );

export function Activity() {
  const { t, formatDate, formatRelative } = useI18n();
  const [older, setOlder] = useState<ActivityItem[]>([]);

  const feed = useLoad(() => studio.activity({ take: 50 }), []);
  const first = feed.data ?? [];
  const all = [...first, ...older];

  useLedge(
    <>
      <span className="engraved">{t("activity.title")}</span>
      <div className="ledge-end">
        <button
          type="button"
          className="act"
          onClick={() => {
            setOlder([]);
            feed.reload();
          }}
        >
          {t("common.refresh")}
        </button>
      </div>
    </>,
    [t],
  );

  const more = async () => {
    const last = all[all.length - 1];
    if (!last) return;
    const next = await studio.activity({ before: last.sequence, take: 50 });
    setOlder([...older, ...next]);
  };

  // Days are the natural grouping: people remember "yesterday", not a timestamp.
  const days = new Map<string, ActivityItem[]>();
  for (const item of all) {
    const day = item.occurredAtUtc.slice(0, 10);
    days.set(day, [...(days.get(day) ?? []), item]);
  }

  return (
    <div className="stack loose">
      <div className="heading">
        <h1>{t("activity.title")}</h1>
        <span className="engraved">{t("activity.said")}</span>
      </div>

      {feed.loading ? (
        <Wiping rows={6} />
      ) : all.length === 0 ? (
        <Nothing title={t("activity.empty")}>{t("activity.emptySaid")}</Nothing>
      ) : (
        <>
          {[...days.entries()].map(([day, items]) => (
            <section className="band" key={day}>
              <span className="engraved">{formatDate(`${day}T00:00:00Z`)}</span>
              <div className="rows">
                {items.map((item) => {
                  const sentence = saying(t, item.summary);
                  const kind = named(t, item.action);
                  // The second line names the kind of thing that happened. When the
                  // sentence above already opens with those words there is nothing
                  // left to add, and a board that says it twice reads as a fault.
                  const echoes =
                    kind !== "" &&
                    sentence.toLowerCase().startsWith(kind.toLowerCase());

                  return (
                    <div className="row" key={item.sequence}>
                      <span className="row-main">
                        <span className="row-title">{sentence}</span>
                        {echoes ? null : (
                          <span
                            className="quiet"
                            style={{ fontSize: "var(--t-sm)" }}
                          >
                            {kind}
                          </span>
                        )}
                      </span>
                      <span className="row-side">
                        <span
                          className="quiet"
                          style={{ fontSize: "var(--t-sm)" }}
                        >
                          {item.actor?.name ?? ""}
                        </span>
                        <time
                          className="quiet"
                          style={{ fontSize: "var(--t-sm)" }}
                          dateTime={item.occurredAtUtc}
                        >
                          {formatRelative(item.occurredAtUtc)}
                        </time>
                      </span>
                    </div>
                  );
                })}
              </div>
            </section>
          ))}

          <button
            type="button"
            className="act"
            style={{ justifySelf: "start" }}
            onClick={() => void more()}
          >
            {t("activity.more")}
          </button>
        </>
      )}
    </div>
  );
}
