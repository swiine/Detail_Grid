;;; ------------------------------------------------------------------------
;;; STRIPMTEXT.LSP
;;;
;;; Command: STRIPMTEXT
;;;
;;; Removes all inline (in-editor) formatting from the selected MText
;;; objects so the text goes back to the defaults of its MText object
;;; and text style: font, height, color, width factor, oblique angle,
;;; tracking, alignment, underline / overline / strikethrough,
;;; paragraph indents & tabs, and stacked fractions.
;;;
;;; Kept as-is:
;;;   \P  paragraph breaks        \~  non-breaking spaces
;;;   \N  column breaks           \\ \{ \}  literal backslash / braces
;;;   \U+xxxx unicode characters  %%c %%d %%p special characters
;;;
;;; Stacked text (\S1/2; \S1#2; \S1^2;) becomes plain "1/2".
;;;
;;; Usage: APPLOAD this file, then type STRIPMTEXT and select MText
;;;        (pre-selection works too). Undo reverts it in one step.
;;; ------------------------------------------------------------------------

(vl-load-com)

;;; Turns the inside of a stack code (e.g. "1/2", "1#2", "+0.1^-0.1")
;;; into plain text.
(defun stripmtext:unstack (s / i len ch top bot sep)
  (setq i 1 len (strlen s) top "" bot "" sep nil)
  (while (<= i len)
    (setq ch (substr s i 1))
    (cond
      ;; escaped character inside a stack, e.g. \/ or \^
      ((and (= ch "\\") (< i len))
       (setq ch (substr s (1+ i) 1) i (1+ i))
       (if sep (setq bot (strcat bot ch)) (setq top (strcat top ch))))
      ((and (not sep) (member ch '("/" "#" "^")))
       (setq sep T))
      (sep (setq bot (strcat bot ch)))
      (T (setq top (strcat top ch))))
    (setq i (1+ i)))
  (if (and (/= top "") (/= bot ""))
    (strcat top "/" bot)
    (strcat top bot)))

;;; Returns STR with all MText formatting codes removed.
(defun stripmtext:clean (str / len i ch nx out j)
  (setq len (strlen str) i 1 out "")
  (while (<= i len)
    (setq ch (substr str i 1))
    (cond
      ((= ch "\\")
       (setq nx (substr str (1+ i) 1))
       (cond
         ;; escapes / breaks we keep
         ((member nx '("\\" "{" "}" "P" "~" "N"))
          (setq out (strcat out ch nx) i (+ i 2)))
         ;; on/off toggles: underline, overline, strikethrough
         ((member nx '("L" "l" "O" "o" "K" "k"))
          (setq i (+ i 2)))
         ;; codes that run up to a ';'
         ;; A=align C/c=color F/f=font H=height Q=oblique T=tracking
         ;; W=width p=paragraph (indents, tabs, line spacing)
         ((member nx '("A" "C" "c" "F" "f" "H" "Q" "T" "W" "p"))
          (setq j (vl-string-search ";" str (1+ i)))
          (setq i (if j (+ j 2) (1+ len))))
         ;; stacked text
         ((= nx "S")
          (setq j (vl-string-search ";" str (1+ i)))
          (if j
            (setq out (strcat out
                              (stripmtext:unstack
                                (substr str (+ i 2) (- j i 1))))
                  i   (+ j 2))
            (setq out (strcat out (stripmtext:unstack (substr str (+ i 2))))
                  i   (1+ len))))
         ;; anything else (\U+xxxx, \M+, ...) is real text - keep it
         (T (setq out (strcat out ch) i (1+ i)))))
      ;; unescaped braces only group formatting - drop them
      ((member ch '("{" "}"))
       (setq i (1+ i)))
      (T (setq out (strcat out ch) i (1+ i)))))
  out)

(defun c:STRIPMTEXT (/ *error* doc ss n obj old new cnt skipped)
  (setq doc (vla-get-ActiveDocument (vlax-get-acad-object)))
  (defun *error* (msg)
    (vla-EndUndoMark doc)
    (if (not (wcmatch (strcase msg) "*CANCEL*,*QUIT*,*EXIT*"))
      (princ (strcat "\nError: " msg)))
    (princ))
  (prompt "\nSelect MText to strip formatting from: ")
  (if (setq ss (ssget '((0 . "MTEXT"))))
    (progn
      (vla-StartUndoMark doc)
      (setq n 0 cnt 0 skipped 0)
      (repeat (sslength ss)
        (setq obj (vlax-ename->vla-object (ssname ss n))
              old (vla-get-TextString obj)
              new (stripmtext:clean old))
        (if (/= old new)
          (if (vl-catch-all-error-p
                (vl-catch-all-apply 'vla-put-TextString (list obj new)))
            (setq skipped (1+ skipped))   ; e.g. on a locked layer
            (setq cnt (1+ cnt))))
        (setq n (1+ n)))
      (vla-EndUndoMark doc)
      (princ (strcat "\nStripped formatting from "
                     (itoa cnt) " of " (itoa (sslength ss)) " MText object(s)."
                     (if (> skipped 0)
                       (strcat " " (itoa skipped) " skipped (locked layer?).")
                       "")))))
  (princ))

(princ "\nSTRIPMTEXT loaded. Type STRIPMTEXT to run.")
(princ)
