import { expect, test } from "@playwright/test";
import type { PlantingRsDto } from "../src/app/generated/model/plantingRsDto";

const north = {
  id: "ad31b935-6b1c-4669-8c92-fb1c8489900f",
  name: "北側田",
  areaSquareMeters: 1000,
};
const south = {
  id: "ad31b935-6b1c-4669-8c92-fb1c84899001",
  name: "南側田",
  areaSquareMeters: 800,
};

const planting = (
  id: string,
  fieldId: string,
  cropName: string,
  areaSquareMeters: number,
  plannedPlantingDate: string | null,
  actualPlantingDate: string | null,
  status: PlantingRsDto["status"] = "growing",
): PlantingRsDto => ({
  id,
  fieldId,
  cropName,
  areaSquareMeters,
  plannedPlantingDate,
  actualPlantingDate,
  status,
  variety: null,
  notes: null,
});

test("跨田區清單可合併搜尋與篩選，任一栽種日期命中便保留該列", async ({
  page,
}) => {
  const fields = [
    {
      ...north,
      plantings: [
        planting("1", north.id, "番茄", 350, "2025-07-22", "2025-08-25"),
        planting("2", north.id, "小白菜", 80, "2025-08-22", null, "planned"),
        planting("3", north.id, "玉米", 180, "2025-07-20", "2025-08-25"),
      ],
    },
    {
      ...south,
      plantings: [
        planting("4", south.id, "番茄", 240, "2025-07-20", "2025-08-01"),
        planting("5", south.id, "青江菜", 120, "2025-07-25", "2025-08-22"),
        planting("6", south.id, "洋蔥", 50, null, null, "planned"),
      ],
    },
  ];
  await page.route("**/api/v1/**", (route) => {
    switch (new URL(route.request().url()).pathname) {
      case "/api/v1/auth/csrf":
      case "/api/v1/auth/session":
        return route.fulfill({ status: 204 });
      case "/api/v1/auth/refresh":
        return route.fulfill({
          json: { accessToken: "test-token", sessionId: "test-session" },
        });
      case "/api/v1/overview":
        return route.fulfill({ json: fields });
      default:
        return route.fulfill({ status: 404 });
    }
  });

  await page.goto("/dashboard/plantings");
  const table = page.getByRole("table", { name: "栽培紀錄" });
  const rows = table
    .locator("tbody tr")
    .filter({ has: page.getByRole("rowheader") });
  const sort = (name: string) =>
    table.getByRole("button", { name, exact: true });
  await expect(rows).toHaveCount(6);
  await expect(
    table.getByRole("columnheader", { name: "實際栽種日" }),
  ).toHaveAttribute("aria-sort", "descending");
  await expect(rows.first()).toContainText("2025-08-25");

  const start = page.getByLabel("栽培日期起日");
  const end = page.getByLabel("栽培日期迄日");
  await start.fill("2025-07-22");
  await end.fill("2025-08-22");
  await expect(rows).toHaveCount(4);
  await expect(rows.filter({ hasText: "番茄" })).toHaveCount(2);
  await expect(rows.filter({ hasText: "小白菜" })).toHaveCount(1);
  await expect(rows.filter({ hasText: "青江菜" })).toHaveCount(1);
  await expect(rows.filter({ hasText: /玉米|洋蔥/ })).toHaveCount(0);

  const keyword = page.getByRole("searchbox", { name: "搜尋田區或作物名稱" });
  await keyword.fill("  南側  ");
  await expect(rows).toHaveCount(2);
  await keyword.fill("番");
  await expect(rows).toHaveCount(2);
  await page.getByLabel("田區", { exact: true }).selectOption(north.id);
  await expect(rows).toHaveCount(1);
  await page.getByLabel("作物", { exact: true }).selectOption("小白菜");
  await expect(page.getByRole("status")).toHaveText("查無符合條件的栽培紀錄。");
  await keyword.fill("");
  await expect(rows).toHaveCount(1);
  await expect(rows.first()).toContainText("小白菜");

  await start.fill("2025-08-23");
  await expect(page.getByRole("alert")).toHaveText(
    "栽培日期的起日不可晚於迄日。",
  );
  await page.getByRole("button", { name: "清除篩選" }).click();
  await expect(rows).toHaveCount(6);

  await sort("栽培面積").click();
  await expect(rows.first()).toContainText("洋蔥");
  await sort("栽培面積").click();
  await expect(rows.first()).toContainText("350 m²");
  await sort("預定栽種日").click();
  await expect(rows.last()).toContainText("洋蔥");
  await sort("預定栽種日").click();
  await expect(rows.first()).toContainText("小白菜");
  await expect(rows.last()).toContainText("洋蔥");
});

test("空清單可直接新增，換田區後儲存並顯示新紀錄", async ({ page }) => {
  let saved: PlantingRsDto | undefined;
  await page.route("**/api/v1/**", (route) => {
    const request = route.request();
    switch (new URL(request.url()).pathname) {
      case "/api/v1/auth/csrf":
      case "/api/v1/auth/session":
        return route.fulfill({ status: 204 });
      case "/api/v1/auth/refresh":
        return route.fulfill({
          json: { accessToken: "test-token", sessionId: "test-session" },
        });
      case "/api/v1/overview":
        return route.fulfill({
          json: [
            { ...north, plantings: [] },
            { ...south, plantings: saved ? [saved] : [] },
          ],
        });
      case "/api/v1/fields":
        return route.fulfill({ json: { fields: [north, south] } });
      case "/api/v1/fields/" + north.id + "/plantings":
        return route.fulfill({
          json: {
            areaSquareMeters: 1000,
            allocatedAreaSquareMeters: 0,
            remainingAreaSquareMeters: 1000,
            plantings: [],
          },
        });
      case "/api/v1/fields/" + south.id + "/plantings":
        if (request.method() === "POST") {
          const body = request.postDataJSON();
          expect(body).toMatchObject({
            cropName: "小白菜",
            areaSquareMeters: 30,
            status: "planned",
          });
          saved = { ...body, id: "new-planting", fieldId: south.id };
          return route.fulfill({ status: 201, json: saved });
        }
        return route.fulfill({
          json: {
            areaSquareMeters: 800,
            allocatedAreaSquareMeters: 0,
            remainingAreaSquareMeters: 800,
            plantings: [],
          },
        });
      default:
        return route.fulfill({ status: 404 });
    }
  });

  await page.goto("/dashboard/plantings");
  await expect(
    page.getByRole("heading", { name: "目前還沒有栽培紀錄" }),
  ).toBeVisible();
  await page.getByRole("link", { name: "新增栽培紀錄" }).click();
  const picker = page.getByLabel("選擇田區", { exact: true });
  const save = page.getByRole("button", { name: "儲存", exact: true });
  await expect(picker).toHaveValue("");
  await expect(save).toBeDisabled();
  await page.goto("/dashboard/plantings?fieldId=" + north.id);
  await page.getByRole("link", { name: "新增栽培紀錄" }).click();
  await expect(picker).toHaveValue(north.id);
  await page.getByLabel("作物名稱（必填）").fill("小白菜");
  await picker.selectOption(south.id);
  await expect(
    page.getByText(/已分配面積 0 m²、 剩餘面積 800 m²/),
  ).toBeVisible();
  await page.getByLabel("占用面積（m²，必填）").fill("30");
  await save.click();
  await expect(page.getByRole("row", { name: /南側田 小白菜/ })).toBeVisible();
  await expect(page.getByLabel("田區", { exact: true })).toHaveValue(south.id);
  await expect(page.getByRole("status")).toHaveText("已新增栽培紀錄");
});
