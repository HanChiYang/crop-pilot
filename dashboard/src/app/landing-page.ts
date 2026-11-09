import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';

@Component({
  selector: 'app-landing-page',
  imports: [RouterLink],
  template: `
    <header class="landing-header">
      <a routerLink="/" class="brand"><span class="brand-mark">C</span> CropPilot</a>
      <a class="button button-outline" routerLink="/login">登入</a>
    </header>
    <main class="landing-main">
      <div class="hero">
        <section>
          <span class="tag">農場管理</span>
          <h1>掌握每塊田的<br /><em>栽培進度</em></h1>
          <p>整理田區、記下栽培計畫，<br />在總覽查看各塊田目前種了什麼。</p>
          <a class="button" routerLink="/login">登入 CropPilot</a>
        </section>
        <section class="sample panel" aria-label="田區總覽示意">
          <header><h2>田區總覽</h2><span>示意資料</span></header>
          <article><div><strong>北側田</strong><p>番茄 · 1,200 m²</p></div><span class="tag planned">規劃中</span></article>
          <article><div><strong>南側田</strong><p>小白菜 · 850 m²</p></div><span class="tag">栽培中</span></article>
        </section>
      </div>
      <div class="feature-grid">
        <section class="panel"><h2>田區管理</h2><p>記錄田區名稱、面積與座標。</p></section>
        <section class="panel"><h2>栽培規劃</h2><p>為田區新增作物與預定栽種日。</p></section>
        <section class="panel"><h2>田區總覽</h2><p>一眼查看規劃中和栽培中的作物。</p></section>
      </div>
    </main>
  `,
})
export class LandingPage {}
