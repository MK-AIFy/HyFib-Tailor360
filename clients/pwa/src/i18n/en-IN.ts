import { messagesEn } from './messages'

/**
 * English (India) — the source of truth for every user-visible string.
 *
 * A thin composer over `messages/`, and deliberately so: this file and its Tamil twin would
 * otherwise be the two files every screen has to edit, which makes them the two files every change
 * collides on. The strings themselves live one family per file.
 */
export const enIN = messagesEn

/** Every key the application may ask for. A missing key in another catalogue is a type error. */
export type MessageKey = keyof typeof enIN

/** The shape every locale catalogue must satisfy. */
export type MessageCatalogue = Record<MessageKey, string>
