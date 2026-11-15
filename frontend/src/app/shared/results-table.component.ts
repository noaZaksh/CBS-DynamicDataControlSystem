import { Component, computed, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ResultColumn, ResultRow } from '../core/models';

const PAGE_SIZE = 25;

/**
 * טבלת תוצאות לשימוש חוזר: חיפוש חופשי, סינון לפי סיבת חריגה, מיון, דפדוף וייצוא ל-CSV.
 * העמודות דינאמיות - נקבעות לפי הבקרה (שדות תצוגה + שדות שמשתתפים בתנאים).
 */
@Component({
  selector: 'app-results-table',
  standalone: true,
  imports: [FormsModule],
  templateUrl: './results-table.component.html',
  styleUrl: './results-table.component.scss',
})
export class ResultsTableComponent {
  readonly columns = input.required<ResultColumn[]>();
  readonly rows = input.required<ResultRow[]>();
  readonly fileName = input('results');

  readonly search = signal('');
  readonly reasonFilter = signal('');
  readonly sort = signal<{ key: string; dir: 1 | -1 } | null>(null);
  readonly page = signal(0);

  readonly titleColumn = computed(() => this.columns().find(c => c.role === 'title'));
  readonly titleLabel = computed(() => this.titleColumn()?.label ?? 'רשומה');
  readonly dataColumns = computed(() => this.columns().filter(c => c.role !== 'title'));

  /** סוגי סיבות = שם השדה שלפני הנקודתיים ("גיל: 16 (קטן מ-18)" → "גיל"). */
  readonly reasonTypes = computed(() =>
    [...new Set(this.rows().flatMap(r => r.reasons.map(reasonType)))].sort());

  readonly filtered = computed(() => {
    const q = this.search().trim().toLowerCase();
    const reason = this.reasonFilter();
    let rows = this.rows().filter(r =>
      (!reason || r.reasons.some(x => reasonType(x) === reason)) &&
      (!q || [r.title ?? '', r.key, ...Object.values(r.values).map(v => String(v ?? '')), ...r.reasons]
        .some(text => text.toLowerCase().includes(q))));

    const sort = this.sort();
    if (sort) {
      const get = (r: ResultRow) => sort.key === '__title' ? r.title ?? '' : r.values[sort.key];
      rows = [...rows].sort((a, b) => compare(get(a), get(b)) * sort.dir);
    }
    return rows;
  });

  readonly pageCount = computed(() => Math.max(1, Math.ceil(this.filtered().length / PAGE_SIZE)));
  readonly pageRows = computed(() => this.filtered().slice(this.page() * PAGE_SIZE, (this.page() + 1) * PAGE_SIZE));

  sortBy(key: string) {
    const s = this.sort();
    this.sort.set(s?.key === key ? (s.dir === 1 ? { key, dir: -1 } : null) : { key, dir: 1 });
  }

  arrow(key: string): string {
    const s = this.sort();
    return s?.key === key ? (s.dir === 1 ? '▲' : '▼') : '';
  }

  format(value: unknown, column: ResultColumn): string {
    if (value === null || value === undefined || value === '') return '—';
    if (column.dataType === 'boolean') return value ? 'כן' : 'לא';
    if (column.dataType === 'number' && typeof value === 'number') return value.toLocaleString('he-IL');
    if (column.dataType === 'date' && typeof value === 'string') {
      const [y, m, d] = value.split('-');
      return d ? `${d}/${m}/${y}` : value;
    }
    return String(value);
  }

  exportCsv() {
    const headers = [this.titleLabel(), ...this.dataColumns().map(c => c.label), 'סיבת החריגה'];
    const lines = this.filtered().map(r => [
      r.title ?? r.key,
      ...this.dataColumns().map(c => this.format(r.values[c.key], c)),
      r.reasons.join(' | '),
    ]);
    const csv = [headers, ...lines].map(cols => cols.map(csvCell).join(',')).join('\r\n');
    // BOM כדי ש-Excel יציג עברית נכון
    const blob = new Blob(['﻿' + csv], { type: 'text/csv;charset=utf-8' });
    const a = document.createElement('a');
    a.href = URL.createObjectURL(blob);
    a.download = `${this.fileName()}.csv`;
    a.click();
    URL.revokeObjectURL(a.href);
  }
}

function reasonType(reason: string): string {
  const i = reason.indexOf(':');
  return i > 0 ? reason.slice(0, i) : reason;
}

function compare(a: unknown, b: unknown): number {
  if (a === b) return 0;
  if (a === null || a === undefined) return 1;
  if (b === null || b === undefined) return -1;
  if (typeof a === 'number' && typeof b === 'number') return a - b;
  return String(a).localeCompare(String(b), 'he');
}

function csvCell(value: string): string {
  return /[",\r\n]/.test(value) ? `"${value.replace(/"/g, '""')}"` : value;
}
