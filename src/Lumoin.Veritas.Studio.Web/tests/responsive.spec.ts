import { expect, test, type Page } from '@playwright/test';

const visiblePanels = '.body > .panel:visible';

async function expectNoRootOverflow(page: Page): Promise<void> {
  const geometry = await page.evaluate(() => ({
    clientWidth: document.documentElement.clientWidth,
    scrollWidth: document.documentElement.scrollWidth
  }));
  expect(geometry.scrollWidth).toBeLessThanOrEqual(geometry.clientWidth);
}

async function panelGeometry(page: Page): Promise<Record<string, DOMRect>> {
  return page.evaluate(() => Object.fromEntries(
    ['workspace-editor', 'workspace-results', 'workspace-trace'].map((id) => {
      const panel = document.getElementById(id);
      if (!panel) throw new Error(`Missing responsive panel #${id}`);
      return [id, panel.getBoundingClientRect().toJSON()];
    })
  ));
}

test('keeps the Studio workspace usable across desktop, tablet, phone, and short landscape', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto('/?engine=wasm&dataset=adaptation');
  await expect(page.locator('[data-testid="active-dataset"]')).toHaveText('Water adaptation pathways');
  await expect(page.locator('[data-testid="worlds-strip"]')).toBeVisible();

  const mobileNav = page.locator('[data-testid="mobile-workspace-nav"]');

  // Desktop keeps the three-pane Studio geometry.
  await expect(mobileNav).toBeHidden();
  await expect(page.locator(visiblePanels)).toHaveCount(3);
  await expectNoRootOverflow(page);
  const desktop = await panelGeometry(page);
  expect(desktop['workspace-editor'].left).toBeLessThan(desktop['workspace-results'].left);
  expect(desktop['workspace-results'].left).toBeLessThan(desktop['workspace-trace'].left);

  // Tablet portrait automatically uses the established split: editor beside Results and Why.
  await page.setViewportSize({ width: 820, height: 1180 });
  await expect(mobileNav).toBeHidden();
  await expect(page.locator(visiblePanels)).toHaveCount(3);
  await expectNoRootOverflow(page);
  const tablet = await panelGeometry(page);
  expect(tablet['workspace-editor'].left).toBeLessThan(tablet['workspace-results'].left);
  expect(tablet['workspace-results'].top).toBeLessThan(tablet['workspace-trace'].top);

  // A phone starts in Results and exposes each complete pane through persistent navigation.
  await page.setViewportSize({ width: 390, height: 844 });
  await expect(mobileNav).toBeVisible();
  await expect(mobileNav.getByRole('link')).toHaveText(['Edit', 'Results', 'Why']);
  await expect(page.locator(visiblePanels)).toHaveCount(1);
  await expect(page.locator('#workspace-results')).toBeVisible();
  await expectNoRootOverflow(page);

  await mobileNav.getByRole('link', { name: 'Edit' }).click();
  await expect(page).toHaveURL(/#workspace-editor$/);
  await expect(page.locator(visiblePanels)).toHaveCount(1);
  await expect(page.locator('#workspace-editor')).toBeVisible();

  await mobileNav.getByRole('link', { name: 'Why' }).click();
  await expect(page.locator(visiblePanels)).toHaveCount(1);
  await expect(page.locator('#workspace-trace')).toBeVisible();

  await mobileNav.getByRole('link', { name: 'Results' }).click();
  await expect(page.locator(visiblePanels)).toHaveCount(1);
  await expect(page.locator('#workspace-results')).toBeVisible();
  await expect.poll(() => page.locator('#graph-canvas').evaluate((canvas) => {
    const bounds = canvas.getBoundingClientRect();
    return bounds.width > 0 && bounds.height > 0;
  })).toBe(true);

  // The scenario dialog remains contained and reachable at phone size.
  await page.locator('[data-testid="world-create"]').click();
  await expect(page.locator('#scenario-dialog')).toBeVisible();
  const dialogFits = await page.locator('#scenario-dialog').evaluate((dialog) => {
    const bounds = dialog.getBoundingClientRect();
    return bounds.left >= 0
      && bounds.right <= window.innerWidth
      && bounds.top >= 0
      && bounds.bottom <= window.innerHeight;
  });
  expect(dialogFits).toBe(true);
  await page.locator('[data-testid="scenario-cancel"]').click();

  // Short landscape also switches to one pane instead of crushing Results between sidebars.
  await page.setViewportSize({ width: 844, height: 390 });
  await expect(mobileNav).toBeVisible();
  await expect(page.locator(visiblePanels)).toHaveCount(1);
  await expect(page.locator('#workspace-results')).toBeVisible();
  await expectNoRootOverflow(page);

  const shellRows = await page.evaluate(() => {
    const worlds = document.querySelector<HTMLElement>('[data-testid="worlds-strip"]')!;
    const nav = document.querySelector<HTMLElement>('[data-testid="mobile-workspace-nav"]')!;
    const body = document.querySelector<HTMLElement>('.body')!;
    const status = document.querySelector<HTMLElement>('.statusbar')!;
    return {
      worldsBottom: worlds.getBoundingClientRect().bottom,
      navTop: nav.getBoundingClientRect().top,
      navBottom: nav.getBoundingClientRect().bottom,
      bodyTop: body.getBoundingClientRect().top,
      bodyBottom: body.getBoundingClientRect().bottom,
      statusTop: status.getBoundingClientRect().top
    };
  });
  expect(shellRows.navTop).toBeGreaterThanOrEqual(shellRows.worldsBottom);
  expect(shellRows.bodyTop).toBeGreaterThanOrEqual(shellRows.navBottom);
  expect(shellRows.statusTop).toBeGreaterThanOrEqual(shellRows.bodyBottom);
});
