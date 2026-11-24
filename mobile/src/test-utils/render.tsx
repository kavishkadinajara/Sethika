import { act, type ReactElement } from "react";
import { createRoot, type TestInstance } from "test-renderer";

/*  A small screen-reading layer over `test-renderer`, the renderer jest-expo already ships (and
    the one React Native Testing Library is built on), so component tests need no new package.
    Queries look at what a donor sees or hears: the text on screen and accessibility labels.
    press() taps through Pressable's own configuration, so a disabled button stays disabled.  */

// React's act() environment, as RNTL sets it.
(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

function textOf(node: TestInstance | string): string {
  return typeof node === "string" ? node : node.children.map(textOf).join("");
}

/** The host Text elements, deepest only, so a nested Text is not counted twice. */
function texts(root: TestInstance): TestInstance[] {
  return root.queryAll((node) => node.type === "Text", { matchDeepestOnly: false }).filter(
    (node) => !node.parent || node.parent.type !== "Text",
  );
}

function matches(value: string, wanted: string | RegExp): boolean {
  return typeof wanted === "string" ? value === wanted : wanted.test(value);
}

export type Screen = ReturnType<typeof screenOf>;

function screenOf(root: TestInstance) {
  const screen = {
    /** Every line of text on screen, in order. */
    allText: (): string[] => texts(root).map(textOf),
    queryText: (wanted: string | RegExp): TestInstance | null => texts(root).find((node) => matches(textOf(node), wanted)) ?? null,
    /** Every Text showing `wanted`, in screen order (a sheet renders after the page under it). */
    allMatching: (wanted: string | RegExp): TestInstance[] => texts(root).filter((node) => matches(textOf(node), wanted)),
    getText(wanted: string | RegExp): TestInstance {
      const found = screen.queryText(wanted);
      if (!found) throw new Error(`No text ${String(wanted)} in:\n${screen.allText().join("\n")}`);
      return found;
    },
    queryLabel: (wanted: string | RegExp): TestInstance | null =>
      root.queryAll((node) => typeof node.props.accessibilityLabel === "string" && matches(node.props.accessibilityLabel, wanted))[0] ?? null,
    getLabel(wanted: string | RegExp): TestInstance {
      const found = screen.queryLabel(wanted);
      if (!found) throw new Error(`No element labelled ${String(wanted)}`);
      return found;
    },
    getTestId(id: string): TestInstance {
      const found = root.queryAll((node) => node.props.testID === id)[0];
      if (!found) throw new Error(`No element with testID ${id}`);
      return found;
    },
    queryTestId: (id: string): TestInstance | null => root.queryAll((node) => node.props.testID === id)[0] ?? null,
    /** The host elements a test picks by their props, for what a donor sees but no text names. */
    where: (matchesProps: (props: Record<string, unknown>) => boolean): TestInstance[] =>
      root.queryAll((node) => matchesProps(node.props as Record<string, unknown>)),
  };
  return screen;
}

/*  Every screen still mounted is unmounted after each test, as RNTL's auto-cleanup does. A screen
    left mounted keeps its timers: `useNow`'s 30-second interval then fired after Jest had torn the
    environment down, and the run failed with every test passed (CI, 2026-11-07).  */
const mounted = new Set<ReturnType<typeof createRoot>>();

afterEach(() => {
  for (const root of mounted) act(() => root.unmount());
  mounted.clear();
});

/** Renders `element` and lets its effects and first fetches settle. */
export async function render(
  element: ReactElement,
): Promise<Screen & { rerender: (next: ReactElement) => Promise<void>; unmount: () => void }> {
  const root = createRoot();
  mounted.add(root);
  await act(async () => {
    root.render(element);
  });
  await settle();
  return {
    ...screenOf(root.container),
    /** Renders again with new props (or after a mocked hook changed), keeping the state. */
    async rerender(next: ReactElement) {
      await act(async () => {
        root.render(next);
      });
      await settle();
    },
    unmount: () => {
      mounted.delete(root);
      act(() => root.unmount());
    },
  };
}

/** Waits for pending promises (stubbed fetches) and the renders they cause. */
export async function settle(): Promise<void> {
  for (let i = 0; i < 5; i++) {
    await act(async () => {
      await new Promise((resolve) => setTimeout(resolve, 0));
    });
  }
}

/** The Pressable an element is in: the nearest host element that Pressability configured. */
function pressable(node: TestInstance): TestInstance {
  for (let current: TestInstance | null = node; current; current = current.parent) {
    if (current.props.onStartShouldSetResponder?.testOnly_pressabilityConfig) return current;
  }
  throw new Error("Not inside a Pressable");
}

/** Taps the Pressable that holds `node`, unless it is disabled. */
export async function press(node: TestInstance): Promise<void> {
  const config = pressable(node).props.onStartShouldSetResponder.testOnly_pressabilityConfig() as {
    disabled?: boolean | null;
    onPress?: (event: unknown) => void;
  };
  if (config.disabled) return;
  await act(async () => {
    config.onPress?.({ nativeEvent: {} });
  });
  await settle();
}

/** Whether the Pressable that holds `node` would ignore a tap. */
export function isDisabled(node: TestInstance): boolean {
  const config = pressable(node).props.onStartShouldSetResponder.testOnly_pressabilityConfig() as { disabled?: boolean | null };
  return config.disabled === true;
}

/** Types into a TextInput (found by its accessibility label). */
export async function type(input: TestInstance, text: string): Promise<void> {
  await act(async () => {
    input.props.onChangeText?.(text);
  });
}

// ---- the API, as the screens' boundary sees it --------------------------------------------------

export function json(status: number, body: unknown): Response {
  return new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });
}
