"use client";

import { useLayoutEffect, useRef, type RefObject } from "react";

/*  The glide (docs/DESIGN.md, Motion): the active pill moves the way a drop of water would. Its
    leading edge sets off first and fast, its trailing edge follows a little later and catches up,
    so for a moment the pill stretches across the gap between the old item and the new one.

    No animation library: the two edges are registered custom properties (--glide-start,
    --glide-end in globals.css), so the browser can transition each one with its own duration
    and easing. The pill is a copy of the menu in the active colours, clipped to the band between
    the two edges, so a label turns ivory exactly where the pill is under it, mid-glide too.   */

const LEAD = "360ms cubic-bezier(0.25, 1, 0.5, 1)"; // quick out: the edge that leaves first
const TRAIL = "550ms cubic-bezier(0.76, 0, 0.24, 1)"; // in-out: the edge that catches up

type Edges = { start: number; end: number };

/*  Where each menu's pill last was. Pages in different route groups have layouts of their own,
    so going from the dashboard to a (wide) page mounts a new shell, and a new menu with it. The
    new menu starts its pill where the old one left it, so the glide is not lost on the way.  */
const remembered = new Map<string, Edges>();

/**
 * Keeps the glide pill of `track` on the element inside it that matches `[data-glide-active]`.
 * Measures again when the track resizes (fonts arriving, a language with longer words), without
 * animating: only a change of the active item glides. `id` names the menu, for `remembered`.
 */
export function useGlide<T extends HTMLElement>(
  id: string,
  activeKey: string | null,
): RefObject<T | null> {
  const track = useRef<T | null>(null);
  const last = useRef<Edges | null>(null);

  useLayoutEffect(() => {
    const el = track.current;
    if (el === null) return;

    const place = (edges: Edges) => {
      el.style.setProperty("--glide-start", `${edges.start}px`);
      el.style.setProperty("--glide-end", `${edges.end}px`);
    };

    const measure = (animate: boolean) => {
      const item = el.querySelector<HTMLElement>("[data-glide-active]");
      let prev = last.current;
      if (prev === null && animate) {
        // A fresh mount: start from where this menu's pill was in the shell before, if anywhere.
        prev = remembered.get(id) ?? null;
        if (prev !== null) {
          el.style.transition = "none";
          place(prev);
          void el.offsetWidth; // commit the old place, so the change below transitions from it
        }
      }
      let next: Edges;
      if (item === null) {
        // No item here (a page outside the menu): the pill closes up where it was.
        const mid = prev === null ? 0 : (prev.start + prev.end) / 2;
        next = { start: mid, end: mid };
      } else {
        next = { start: item.offsetTop, end: item.offsetTop + item.offsetHeight };
      }

      // Nothing moved (the observer's first call, a resize that left the item in place): leave
      // any glide under way alone.
      if (prev !== null && next.start === prev.start && next.end === prev.end) {
        last.current = next;
        return;
      }
      if (!animate || prev === null) {
        el.style.transition = "none";
      } else {
        // Moving down the end edge leads; moving back, the start edge leads.
        const forward = next.start > prev.start;
        el.style.transition = forward
          ? `--glide-end ${LEAD}, --glide-start ${TRAIL}`
          : `--glide-start ${LEAD}, --glide-end ${TRAIL}`;
      }
      place(next);
      last.current = next;
      remembered.set(id, next);
    };

    measure(true);
    const observer = new ResizeObserver(() => measure(false));
    observer.observe(el);
    return () => observer.disconnect();
  }, [id, activeKey]);

  return track;
}
