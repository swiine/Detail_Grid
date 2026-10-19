;;; CircleWipeout.lsp
;;; Command: CWIPE
;;; Creates circular wipeouts. Either:
;;;   - select existing circles to turn into wipeouts (circles are kept), or
;;;   - press Enter to pick a centre point and radius.
;;; Wipeouts can only have straight edges, so the circle is drawn as a
;;; 72-sided polygon, which looks round at normal zoom.

(vl-load-com)

(setq CircleWipeout:sides 72)

;; Make a wipeout from centre (WCS) and radius, on the current layer
(defun CircleWipeout:make (cen rad / pts i ang pl)
  (setq i 0)
  (repeat CircleWipeout:sides
    (setq ang (* i (/ (* 2 pi) CircleWipeout:sides))
          pts (cons (list 10 (+ (car cen) (* rad (cos ang)))
                             (+ (cadr cen) (* rad (sin ang))))
                    pts)
          i   (1+ i)))
  (setq pl (entmakex (append (list '(0 . "LWPOLYLINE")
                                   '(100 . "AcDbEntity")
                                   '(100 . "AcDbPolyline")
                                   (cons 90 CircleWipeout:sides)
                                   '(70 . 1)
                                   (cons 38 (caddr cen)))
                             (reverse pts))))
  (if pl (command "_.WIPEOUT" "_P" pl "_Y")))

(defun c:CWIPE (/ *error* doc oldEcho ss i ed cen rad)
  (setq doc     (vla-get-ActiveDocument (vlax-get-acad-object))
        oldEcho (getvar "CMDECHO"))

  (defun *error* (msg)
    (setvar "CMDECHO" oldEcho)
    (vla-EndUndoMark doc)
    (if (not (wcmatch (strcase msg) "*CANCEL*,*QUIT*,*EXIT*"))
      (princ (strcat "\nError: " msg)))
    (princ))

  (vla-StartUndoMark doc)
  (setvar "CMDECHO" 0)

  (princ "\nSelect circles to convert, or press Enter to pick centre/radius: ")
  (if (setq ss (ssget '((0 . "CIRCLE"))))
    (progn
      (setq i 0)
      (repeat (sslength ss)
        (setq ed (entget (ssname ss i)))
        (CircleWipeout:make (trans (cdr (assoc 10 ed)) (ssname ss i) 0)
                            (cdr (assoc 40 ed)))
        (setq i (1+ i)))
      (princ (strcat "\n" (itoa (sslength ss)) " circular wipeout(s) created.")))
    (while (setq cen (getpoint "\nCentre point <exit>: "))
      (if (setq rad (getdist cen "\nRadius: "))
        (CircleWipeout:make (trans cen 1 0) rad))))

  (setvar "CMDECHO" oldEcho)
  (vla-EndUndoMark doc)
  (princ))

(princ "\nCircleWipeout loaded. Type CWIPE to run.")
(princ)
